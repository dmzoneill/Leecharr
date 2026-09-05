// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.GeoIp;

public class IP2LocationGeoIpProvider : IGeoIpProvider, IDisposable
{
    private readonly IDiskProvider diskProvider;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger;
    private readonly object @lock = new();

    private FileStream fileStream;
    private BinaryReader binaryReader;
    private string resolvedDatabasePath;
    private byte dbType;
    private byte dbColumn;
    private uint ipv4Count;
    private uint baseAddress;
    private uint ipv6Count;
    private uint baseAddressIPv6;
    private bool disposed;

    public string ProviderId => "IP2Location";

    public string DisplayName => "IP2Location Binary (.BIN)";

    public string Version => "1.0";

    public bool IsAvailable => !string.IsNullOrEmpty(this.GetDatabasePath());

    public GeoIpCapabilities Capabilities => GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.OfflineDatabase;

    public IP2LocationGeoIpProvider(IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
    {
        this.diskProvider = diskProvider;
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public virtual string GetDatabasePath()
    {
        var candidates = new List<string>
        {
            "/config/GeoIP/IP2LOCATION-LITE-DB1.BIN",
            "/config/GeoIP/IP2LOCATION-LITE-DB11.BIN",
            "/config/GeoIP/IP2LOCATION-LITE-DB3.BIN",
            "/config/GeoIP/IP2Location.BIN",
            "/config/IP2Location.BIN",
            Path.Combine(this.appFolderInfo.AppDataFolder, "GeoIP", "IP2LOCATION-LITE-DB1.BIN"),
            Path.Combine(this.appFolderInfo.AppDataFolder, "GeoIP", "IP2Location.BIN"),
            Path.Combine(this.appFolderInfo.AppDataFolder, "IP2Location.BIN"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IP2Location.BIN"),
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
                StatusMessage = "IP2Location .BIN database not found. Place the file in /config/GeoIP/ or AppData.",
                Warnings = new List<string> { "Database file missing." },
            });
        }

        try
        {
            this.EnsureReader(dbPath);
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = true,
                StatusMessage = $"IP2Location database loaded successfully from {dbPath} (DB Type: {this.dbType}, IPv4 Records: {this.ipv4Count}).",
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Failed to read IP2Location database: {ex.Message}",
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
            lock (this.@lock)
            {
                this.EnsureReader(dbPath);
                if (this.binaryReader == null)
                {
                    return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
                }

                if (parsedIp.IsIPv4MappedToIPv6)
                {
                    parsedIp = parsedIp.MapToIPv4();
                }

                if (parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var ipBytes = parsedIp.GetAddressBytes();
                    Array.Reverse(ipBytes);
                    var ipNum = BitConverter.ToUInt32(ipBytes, 0);

                    var low = 0L;
                    var high = (long)this.ipv4Count;
                    var rowSize = (long)this.dbColumn * 4;

                    while (low <= high)
                    {
                        var mid = low + ((high - low) / 2);
                        var rowOffset = (this.baseAddress - 1) + (mid * rowSize);

                        this.fileStream.Seek(rowOffset, SeekOrigin.Begin);
                        var ipFrom = this.binaryReader.ReadUInt32();

                        var ipToOffset = (this.baseAddress - 1) + ((mid + 1) * rowSize);
                        this.fileStream.Seek(ipToOffset, SeekOrigin.Begin);
                        var ipTo = this.binaryReader.ReadUInt32();

                        if (ipNum >= ipFrom && ipNum < ipTo)
                        {
                            var result = this.ReadRecordData(rowOffset, 4);
                            result.IpAddress = ipAddress;
                            return Task.FromResult(result);
                        }

                        if (ipNum < ipFrom)
                        {
                            high = mid - 1;
                        }
                        else
                        {
                            low = mid + 1;
                        }
                    }
                }
                else if (parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && this.ipv6Count > 0 && this.baseAddressIPv6 > 0)
                {
                    var ipNum = new BigInteger(parsedIp.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

                    var low = 0L;
                    var high = (long)this.ipv6Count;
                    var rowSize = 16L + ((long)(this.dbColumn - 1) * 4);

                    while (low <= high)
                    {
                        var mid = low + ((high - low) / 2);
                        var rowOffset = (this.baseAddressIPv6 - 1) + (mid * rowSize);

                        this.fileStream.Seek(rowOffset, SeekOrigin.Begin);
                        var fromBytes = this.binaryReader.ReadBytes(16);
                        if (fromBytes.Length < 16)
                        {
                            break;
                        }

                        var ipFrom = new BigInteger(fromBytes, isUnsigned: true, isBigEndian: false);

                        var ipToOffset = (this.baseAddressIPv6 - 1) + ((mid + 1) * rowSize);
                        this.fileStream.Seek(ipToOffset, SeekOrigin.Begin);
                        var toBytes = this.binaryReader.ReadBytes(16);
                        if (toBytes.Length < 16)
                        {
                            break;
                        }

                        var ipTo = new BigInteger(toBytes, isUnsigned: true, isBigEndian: false);

                        if (ipNum >= ipFrom && ipNum < ipTo)
                        {
                            var result = this.ReadRecordData(rowOffset, 16);
                            result.IpAddress = ipAddress;
                            return Task.FromResult(result);
                        }

                        if (ipNum < ipFrom)
                        {
                            high = mid - 1;
                        }
                        else
                        {
                            low = mid + 1;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error performing IP2Location lookup for IP: {0}", ipAddress);
        }

        return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
    }

    private GeoLocationInfo ReadRecordData(long rowOffset, int ipColumnSize = 4)
    {
        var info = new GeoLocationInfo();
        this.fileStream.Seek(rowOffset + ipColumnSize, SeekOrigin.Begin);

        var countryOffset = this.binaryReader.ReadUInt32();
        if (countryOffset > 0)
        {
            this.fileStream.Seek(countryOffset, SeekOrigin.Begin);
            var len = this.binaryReader.ReadByte();
            var countryCodeBytes = this.binaryReader.ReadBytes(len);
            info.CountryCode = Encoding.ASCII.GetString(countryCodeBytes).Trim();

            var nameLen = this.binaryReader.ReadByte();
            var countryNameBytes = this.binaryReader.ReadBytes(nameLen);
            info.CountryName = Encoding.ASCII.GetString(countryNameBytes).Trim();
        }

        return info;
    }

    private void EnsureReader(string dbPath)
    {
        if (this.binaryReader != null && this.resolvedDatabasePath == dbPath)
        {
            return;
        }

        this.CloseReader();

        this.fileStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        this.binaryReader = new BinaryReader(this.fileStream);
        this.resolvedDatabasePath = dbPath;

        this.dbType = this.binaryReader.ReadByte();
        this.dbColumn = this.binaryReader.ReadByte();
        var year = this.binaryReader.ReadByte();
        var month = this.binaryReader.ReadByte();
        var day = this.binaryReader.ReadByte();
        this.ipv4Count = this.binaryReader.ReadUInt32();
        this.baseAddress = this.binaryReader.ReadUInt32();
        this.ipv6Count = this.binaryReader.ReadUInt32();
        this.baseAddressIPv6 = this.binaryReader.ReadUInt32();
    }

    private void CloseReader()
    {
        this.binaryReader?.Dispose();
        this.binaryReader = null;
        this.fileStream?.Dispose();
        this.fileStream = null;
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            lock (this.@lock)
            {
                this.CloseReader();
            }
        }
    }
}
