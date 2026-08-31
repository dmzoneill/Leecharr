// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
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
            this.reader = new DatabaseReader(dbPath);
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
