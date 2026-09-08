// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.GeoIp;

public class MaxMindGeoIpProvider : IGeoIpProvider, IDisposable
{
    private readonly IDiskProvider diskProvider;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger;
    private readonly object @lock = new();

    private DatabaseReader reader;
    private string resolvedDatabasePath;
    private bool disposed;

    public string ProviderId => "MaxMind";

    public string DisplayName => "MaxMind GeoLite2 / GeoIP2 (.mmdb)";

    public string Version => "2.0";

    public bool IsAvailable => !string.IsNullOrEmpty(this.GetDatabasePath());

    public GeoIpCapabilities Capabilities => GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.Asn | GeoIpCapabilities.OfflineDatabase;

    public MaxMindGeoIpProvider(IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
    {
        this.diskProvider = diskProvider;
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public string GetDatabasePath()
    {
        var candidates = new List<string>
        {
            "/config/GeoIP/GeoLite2-City.mmdb",
            "/config/GeoLite2-City.mmdb",
            Path.Combine(this.appFolderInfo.AppDataFolder, "GeoIP", "GeoLite2-City.mmdb"),
            Path.Combine(this.appFolderInfo.AppDataFolder, "GeoLite2-City.mmdb"),
            Path.Combine(this.appFolderInfo.StartUpFolder, "GeoIP", "GeoLite2-City.mmdb"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeoLite2-City.mmdb"),
        };

        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && this.diskProvider.FileExists(path))
            {
                return path;
            }
        }

        return null;
    }

    public Task<GeoIpHealthResult> ProbeHealthAsync()
    {
        var dbPath = this.GetDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = "MaxMind GeoLite2-City.mmdb database not found. Place the file in /config/GeoIP/GeoLite2-City.mmdb or AppData.",
                Warnings = new List<string> { "Database file missing." },
            });
        }

        try
        {
            this.EnsureReader(dbPath);
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = true,
                StatusMessage = $"MaxMind database loaded successfully from {dbPath}.",
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Failed to read MaxMind database: {ex.Message}",
                Warnings = new List<string> { ex.Message },
            });
        }
    }

    public Task<GeoLocationInfo> LookupAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return Task.FromResult<GeoLocationInfo>(null);
        }

        if (!IPAddress.TryParse(ipAddress, out var parsedIp))
        {
            return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
        }

        if (IsPrivateOrLoopback(parsedIp))
        {
            return Task.FromResult(new GeoLocationInfo
            {
                IpAddress = ipAddress,
                CountryCode = "LAN",
                CountryName = "Local Network",
                City = "Localhost",
            });
        }

        var dbPath = this.GetDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
        }

        try
        {
            var reader = this.EnsureReader(dbPath);
            if (reader == null)
            {
                return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
            }

            if (reader.TryCity(parsedIp, out var city))
            {
                return Task.FromResult(new GeoLocationInfo
                {
                    IpAddress = ipAddress,
                    CountryCode = city.Country?.IsoCode ?? string.Empty,
                    CountryName = city.Country?.Name ?? string.Empty,
                    City = city.City?.Name ?? string.Empty,
                    Region = city.MostSpecificSubdivision?.Name ?? string.Empty,
                    Latitude = city.Location?.Latitude,
                    Longitude = city.Location?.Longitude,
                    TimeZone = city.Location?.TimeZone ?? string.Empty,
                });
            }
        }
        catch (AddressNotFoundException)
        {
            // Expected when IP is not in database
        }
        catch (GeoIP2Exception ex)
        {
            this.logger.Debug(ex, "MaxMind GeoIP lookup exception for IP: {0}", ipAddress);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Unexpected error during MaxMind GeoIP lookup for IP: {0}", ipAddress);
        }

        return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (ip == null)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (!ip.TryWriteBytes(bytes, out _))
            {
                return false;
            }

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
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback))
            {
                return true;
            }

            Span<byte> bytes = stackalloc byte[16];
            if (!ip.TryWriteBytes(bytes, out _))
            {
                return false;
            }

            // Link-local fe80::/10
            if (ip.IsIPv6LinkLocal || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80))
            {
                return true;
            }

            // Site-local fec0::/10
            if (ip.IsIPv6SiteLocal)
            {
                return true;
            }

            // Unique Local Address fc00::/7
            if (ip.IsIPv6UniqueLocal || ((bytes[0] & 0xFE) == 0xFC))
            {
                return true;
            }
        }

        return false;
    }

    private DatabaseReader EnsureReader(string dbPath)
    {
        if (this.reader != null && this.resolvedDatabasePath == dbPath)
        {
            return this.reader;
        }

        lock (this.@lock)
        {
            if (this.reader != null && this.resolvedDatabasePath == dbPath)
            {
                return this.reader;
            }

            this.reader?.Dispose();
            this.reader = new DatabaseReader(dbPath, FileAccessMode.MemoryMapped);
            this.resolvedDatabasePath = dbPath;
            return this.reader;
        }
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            lock (this.@lock)
            {
                this.reader?.Dispose();
                this.reader = null;
            }
        }
    }
}
