// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network.GeoIp;

public class OnlineApiGeoIpProvider : IGeoIpProvider, IDisposable
{
    private const string DefaultEndpointTemplate = "http://ip-api.com/json/{0}?fields=status,message,country,countryCode,region,regionName,city,lat,lon,timezone,isp,as,query";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly Logger logger;
    private readonly ConcurrentDictionary<string, CachedGeoLocation> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object rateLimitLock = new();
    private readonly Queue<DateTime> requestTimestamps = new();
    private readonly int maxRequestsPerMinute = 45;
    private readonly TimeSpan cacheTtl = TimeSpan.FromHours(24);
    private readonly TimeSpan negativeCacheTtl = TimeSpan.FromMinutes(5);
    private readonly string apiEndpointTemplate;
    private bool disposed;

    public string ProviderId => "OnlineApi";

    public string DisplayName => "Zero-Disk Online HTTP Geolocation API";

    public string Version => "1.0";

    public bool IsAvailable => true;

    public GeoIpCapabilities Capabilities => GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.Asn | GeoIpCapabilities.Isp | GeoIpCapabilities.InMemoryCache;

    public string ApiEndpointTemplate => this.apiEndpointTemplate;

    public OnlineApiGeoIpProvider()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, ownsHttpClient: true)
    {
    }

    public OnlineApiGeoIpProvider(HttpClient httpClient, bool ownsHttpClient = false, string apiEndpointTemplate = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        this.apiEndpointTemplate = string.IsNullOrWhiteSpace(apiEndpointTemplate) ? DefaultEndpointTemplate : apiEndpointTemplate;
        this.logger = LogManager.GetCurrentClassLogger();

        if (this.apiEndpointTemplate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            this.logger.Warn(
                "OnlineApiGeoIpProvider is configured with plaintext HTTP endpoint ({0}). Peer IP lookups may be exposed in transit; configure an HTTPS endpoint or use local MaxMind/IP2Location databases for privacy-sensitive environments.",
                this.apiEndpointTemplate);
        }
    }

    public async Task<GeoIpHealthResult> ProbeHealthAsync()
    {
        try
        {
            var probeUrl = string.Format(this.apiEndpointTemplate, "8.8.8.8");
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            using var response = await this.httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return new GeoIpHealthResult
                {
                    IsHealthy = true,
                    StatusMessage = "Online Geolocation API reachable and responding.",
                };
            }

            return new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Online API returned HTTP status code: {(int)response.StatusCode} {response.ReasonPhrase}.",
                Warnings = new List<string> { "HTTP request failed." },
            };
        }
        catch (Exception ex)
        {
            return new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Online API unreachable: {ex.Message}",
                Warnings = new List<string> { ex.Message },
            };
        }
    }

    public async Task<GeoLocationInfo> LookupAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        // 1. Check in-memory LRU cache
        if (this.cache.TryGetValue(ipAddress, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Info;
        }

        // 2. Ignore private / local / loopback IPs
        if (IsPrivateOrLoopback(ipAddress))
        {
            var local = new GeoLocationInfo
            {
                IpAddress = ipAddress,
                CountryCode = "LAN",
                CountryName = "Local Network",
                City = "Localhost",
            };
            this.SetCache(ipAddress, local, this.cacheTtl);
            return local;
        }

        // 3. Rate limiter check without holding lock across HTTP request
        var isRateLimited = false;
        lock (this.rateLimitLock)
        {
            this.PruneRateLimiter();
            if (this.requestTimestamps.Count >= this.maxRequestsPerMinute)
            {
                isRateLimited = true;
            }
            else
            {
                this.requestTimestamps.Enqueue(DateTime.UtcNow);
            }
        }

        if (isRateLimited)
        {
            this.logger.Warn(
                "GeoIP online API rate limit reached ({0} req/min). Skipping online lookup for {1}.",
                this.maxRequestsPerMinute,
                ipAddress);

            var rateLimitedStub = new GeoLocationInfo { IpAddress = ipAddress };
            this.SetCache(ipAddress, rateLimitedStub, this.negativeCacheTtl);
            return rateLimitedStub;
        }

        // 4. Perform outbound HTTP request
        try
        {
            var endpoint = string.Format(this.apiEndpointTemplate, Uri.EscapeDataString(ipAddress));
            using var response = await this.httpClient.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode)
            {
                this.logger.Warn("GeoIP online request for {0} failed with HTTP {1}", ipAddress, response.StatusCode);
                var errorStub = new GeoLocationInfo { IpAddress = ipAddress };
                this.SetCache(ipAddress, errorStub, this.negativeCacheTtl);
                return errorStub;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
            {
                var info = new GeoLocationInfo
                {
                    IpAddress = ipAddress,
                    CountryCode = root.TryGetProperty("countryCode", out var cc) ? cc.GetString() : string.Empty,
                    CountryName = root.TryGetProperty("country", out var cn) ? cn.GetString() : string.Empty,
                    City = root.TryGetProperty("city", out var city) ? city.GetString() : string.Empty,
                    Region = root.TryGetProperty("regionName", out var reg) ? reg.GetString() : string.Empty,
                    Latitude = root.TryGetProperty("lat", out var lat) && lat.ValueKind == JsonValueKind.Number ? lat.GetDouble() : null,
                    Longitude = root.TryGetProperty("lon", out var lon) && lon.ValueKind == JsonValueKind.Number ? lon.GetDouble() : null,
                    Asn = root.TryGetProperty("as", out var asn) ? asn.GetString() : string.Empty,
                    Isp = root.TryGetProperty("isp", out var isp) ? isp.GetString() : string.Empty,
                    TimeZone = root.TryGetProperty("timezone", out var tz) ? tz.GetString() : string.Empty,
                };

                this.SetCache(ipAddress, info, this.cacheTtl);
                return info;
            }

            var failedStub = new GeoLocationInfo { IpAddress = ipAddress };
            this.SetCache(ipAddress, failedStub, this.negativeCacheTtl);
            return failedStub;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Online GeoIP lookup failed for IP {0}", ipAddress);
            var excStub = new GeoLocationInfo { IpAddress = ipAddress };
            this.SetCache(ipAddress, excStub, this.negativeCacheTtl);
            return excStub;
        }
    }

    private void SetCache(string ip, GeoLocationInfo info, TimeSpan ttl)
    {
        if (this.cache.Count > 10000)
        {
            this.cache.Clear();
        }

        this.cache[ip] = new CachedGeoLocation
        {
            Info = info,
            ExpiresAt = DateTime.UtcNow.Add(ttl),
        };
    }

    private void PruneRateLimiter()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        while (this.requestTimestamps.Count > 0 && this.requestTimestamps.Peek() < cutoff)
        {
            this.requestTimestamps.Dequeue();
        }
    }

    private static bool IsPrivateOrLoopback(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return true;
        }

        if (ip == "127.0.0.1" || ip == "::1" || ip.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(ip, out var addr))
        {
            if (addr.IsIPv4MappedToIPv6)
            {
                addr = addr.MapToIPv4();
            }

            if (System.Net.IPAddress.IsLoopback(addr))
            {
                return true;
            }

            var bytes = addr.GetAddressBytes();
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10)
                {
                    return true;
                }

                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }

                // 127.0.0.0/8
                if (bytes[0] == 127)
                {
                    return true;
                }

                // 169.254.0.0/16
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }

                // 0.0.0.0/8
                if (bytes[0] == 0)
                {
                    return true;
                }
            }
            else if (bytes.Length == 16)
            {
                if (addr.Equals(System.Net.IPAddress.IPv6Loopback))
                {
                    return true;
                }

                // Link-local fe80::/10
                if (addr.IsIPv6LinkLocal || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80))
                {
                    return true;
                }

                // Site-local fec0::/10
                if (addr.IsIPv6SiteLocal)
                {
                    return true;
                }

                // Unique Local Address fc00::/7
                if (addr.IsIPv6UniqueLocal || ((bytes[0] & 0xFE) == 0xFC))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            if (this.ownsHttpClient)
            {
                this.httpClient?.Dispose();
            }
        }
    }

    private class CachedGeoLocation
    {
        public GeoLocationInfo Info { get; set; }

        public DateTime ExpiresAt { get; set; }
    }
}
