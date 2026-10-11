// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using MaxMind.GeoIP2.Responses;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.GeoIp;

public class MaxMindGeoIpProvider : IGeoIpProvider, IDisposable
{
    private static readonly string[] GeoLocationDatabaseNames =
    {
        "GeoLite2-City.mmdb",
        "GeoLite2-Country.mmdb",
        "GeoIP2-City.mmdb",
        "GeoIP2-Country.mmdb",
    };

    private static readonly string[] AsnDatabaseNames =
    {
        "GeoLite2-ASN.mmdb",
        "GeoIP2-ASN.mmdb",
    };

    private readonly IDiskProvider diskProvider;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger;
    private readonly object @lock = new();

    private DatabaseReader reader;
    private string resolvedDatabasePath;
    private DatabaseReader asnReader;
    private string resolvedAsnDatabasePath;
    private bool disposed;

    public string ProviderId => "MaxMind";

    public string DisplayName => "MaxMind GeoLite2 / GeoIP2 (.mmdb)";

    public string Version => "2.0";

    public bool IsAvailable => !string.IsNullOrEmpty(this.GetDatabasePath());

    public GeoIpCapabilities Capabilities
    {
        get
        {
            var capabilities = GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.OfflineDatabase;
            if (!string.IsNullOrEmpty(this.GetAsnDatabasePath()))
            {
                capabilities |= GeoIpCapabilities.Asn;
            }

            return capabilities;
        }
    }

    public MaxMindGeoIpProvider(IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
    {
        this.diskProvider = diskProvider;
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public virtual string GetDatabasePath()
    {
        return this.FindDatabasePath(GeoLocationDatabaseNames);
    }

    public virtual string GetAsnDatabasePath()
    {
        return this.FindDatabasePath(AsnDatabaseNames);
    }

    private string FindDatabasePath(string[] databaseNames)
    {
        foreach (var dir in this.GetSearchDirectories())
        {
            foreach (var dbName in databaseNames)
            {
                var fullPath = Path.Combine(dir, dbName);
                if (this.diskProvider.FileExists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private IEnumerable<string> GetSearchDirectories()
    {
        var searchDirectories = new List<string>
        {
            "/config/GeoIP",
            Path.Combine(this.appFolderInfo.AppDataFolder, "GeoIP"),
            "/config",
            this.appFolderInfo.AppDataFolder,
            Path.Combine(this.appFolderInfo.StartUpFolder, "GeoIP"),
            AppDomain.CurrentDomain.BaseDirectory,
        };

        foreach (var dir in searchDirectories)
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                yield return dir;
            }
        }
    }

    public Task<GeoIpHealthResult> ProbeHealthAsync()
    {
        var dbPath = this.GetDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = "MaxMind database (.mmdb) not found. Place GeoLite2-City.mmdb or GeoLite2-Country.mmdb in /config/GeoIP or AppData.",
                Warnings = new List<string> { "Database file missing." },
            });
        }

        try
        {
            lock (this.@lock)
            {
                this.EnsureReader(dbPath);
            }

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

        if (IsLoopbackAddress(parsedIp))
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
            lock (this.@lock)
            {
                var missingDbInfo = new GeoLocationInfo { IpAddress = ipAddress };
                this.ApplyAsnLookup(missingDbInfo, parsedIp);
                ApplyLanFallbackForPrivateAddress(missingDbInfo, parsedIp);
                return Task.FromResult(missingDbInfo);
            }
        }

        try
        {
            lock (this.@lock)
            {
                this.EnsureReader(dbPath);
                if (this.reader == null)
                {
                    var missingReaderInfo = new GeoLocationInfo { IpAddress = ipAddress };
                    this.ApplyAsnLookup(missingReaderInfo, parsedIp);
                    ApplyLanFallbackForPrivateAddress(missingReaderInfo, parsedIp);
                    return Task.FromResult(missingReaderInfo);
                }

                try
                {
                    if (this.reader.TryCity(parsedIp, out var city))
                    {
                        var cityInfo = new GeoLocationInfo
                        {
                            IpAddress = ipAddress,
                            CountryCode = city.Country?.IsoCode ?? string.Empty,
                            CountryName = city.Country?.Name ?? string.Empty,
                            City = city.City?.Name ?? string.Empty,
                            Region = city.MostSpecificSubdivision?.Name ?? string.Empty,
                            Latitude = city.Location?.Latitude,
                            Longitude = city.Location?.Longitude,
                            TimeZone = city.Location?.TimeZone ?? string.Empty,
                        };
                        this.ApplyAsnLookup(cityInfo, parsedIp);
                        return Task.FromResult(cityInfo);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    this.logger.Trace(ex, "MMDB database is Country-level rather than City for {0}", ipAddress);
                }

                if (this.reader.TryCountry(parsedIp, out var country))
                {
                    var countryInfo = new GeoLocationInfo
                    {
                        IpAddress = ipAddress,
                        CountryCode = country.Country?.IsoCode ?? string.Empty,
                        CountryName = country.Country?.Name ?? string.Empty,
                    };
                    this.ApplyAsnLookup(countryInfo, parsedIp);
                    return Task.FromResult(countryInfo);
                }

                var notFoundInfo = new GeoLocationInfo { IpAddress = ipAddress };
                this.ApplyAsnLookup(notFoundInfo, parsedIp);
                ApplyLanFallbackForPrivateAddress(notFoundInfo, parsedIp);
                return Task.FromResult(notFoundInfo);
            }
        }
        catch (AddressNotFoundException ex)
        {
            this.logger.Trace(ex, "IP address not found in MaxMind database: {0}", ipAddress);
        }
        catch (GeoIP2Exception ex)
        {
            this.logger.Debug(ex, "MaxMind GeoIP lookup exception for IP: {0}", ipAddress);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Unexpected error during MaxMind GeoIP lookup for IP: {0}", ipAddress);
        }

        lock (this.@lock)
        {
            var errorInfo = new GeoLocationInfo { IpAddress = ipAddress };
            this.ApplyAsnLookup(errorInfo, parsedIp);
            ApplyLanFallbackForPrivateAddress(errorInfo, parsedIp);
            return Task.FromResult(errorInfo);
        }
    }

    private static bool IsLoopbackAddress(IPAddress ip)
    {
        if (ip == null)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        return IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Loopback);
    }

    private static void ApplyLanFallbackForPrivateAddress(GeoLocationInfo info, IPAddress parsedIp)
    {
        if (info == null || !IsPrivateNonLoopbackAddress(parsedIp) || !string.IsNullOrEmpty(info.CountryCode))
        {
            return;
        }

        info.CountryCode = "LAN";
        info.CountryName = "Local Network";
    }

    private static bool IsPrivateNonLoopbackAddress(IPAddress ip)
    {
        return IsPrivateOrLoopback(ip) && !IsLoopbackAddress(ip);
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

    // Note: Mutates shared DatabaseReader state; caller must hold this.@lock.
    private void EnsureReader(string dbPath)
    {
        if (this.reader != null && this.resolvedDatabasePath == dbPath)
        {
            return;
        }

        this.reader?.Dispose();
        this.reader = new DatabaseReader(dbPath, FileAccessMode.MemoryMapped);
        this.resolvedDatabasePath = dbPath;
    }

    private void EnsureAsnReader(string dbPath)
    {
        if (this.asnReader != null && this.resolvedAsnDatabasePath == dbPath)
        {
            return;
        }

        this.asnReader?.Dispose();
        this.asnReader = new DatabaseReader(dbPath, FileAccessMode.MemoryMapped);
        this.resolvedAsnDatabasePath = dbPath;
    }

    private void ApplyAsnLookup(GeoLocationInfo info, IPAddress parsedIp)
    {
        var asnDbPath = this.GetAsnDatabasePath();
        if (string.IsNullOrEmpty(asnDbPath))
        {
            return;
        }

        try
        {
            this.EnsureAsnReader(asnDbPath);
            if (this.asnReader != null && this.asnReader.TryAsn(parsedIp, out var asn))
            {
                info.Asn = FormatAsn(asn);
            }
        }
        catch (GeoIP2Exception ex)
        {
            this.logger.Debug(ex, "MaxMind ASN lookup exception for IP: {0}", info.IpAddress);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Unexpected error during MaxMind ASN lookup for IP: {0}", info.IpAddress);
        }
    }

    private static string FormatAsn(AsnResponse asn)
    {
        if (asn?.AutonomousSystemNumber == null)
        {
            return string.Empty;
        }

        var prefix = $"AS{asn.AutonomousSystemNumber}";
        return string.IsNullOrWhiteSpace(asn.AutonomousSystemOrganization)
            ? prefix
            : $"{prefix} {asn.AutonomousSystemOrganization}";
    }

    public void Reload()
    {
        lock (this.@lock)
        {
            this.reader?.Dispose();
            this.reader = null;
            this.resolvedDatabasePath = null;
            this.asnReader?.Dispose();
            this.asnReader = null;
            this.resolvedAsnDatabasePath = null;
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
                this.asnReader?.Dispose();
                this.asnReader = null;
            }
        }
    }
}
