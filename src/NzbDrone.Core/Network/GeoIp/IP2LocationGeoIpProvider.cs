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
    private static readonly BigInteger IPv6MaxLimit = (BigInteger.One << 128) - 1;
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
            lock (this.@lock)
            {
                this.EnsureReader(dbPath);
            }

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
                    Span<byte> ipBytes = stackalloc byte[4];
                    if (!parsedIp.TryWriteBytes(ipBytes, out _))
                    {
                        return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
                    }

                    ipBytes.Reverse();
                    var ipNum = BitConverter.ToUInt32(ipBytes);

                    var low = 0L;
                    var high = (long)this.ipv4Count - 1L;
                    var rowSize = (long)this.dbColumn * 4;

                    while (low <= high)
                    {
                        var mid = low + ((high - low) / 2);
                        var rowOffset = (this.baseAddress - 1) + (mid * rowSize);

                        this.fileStream.Seek(rowOffset, SeekOrigin.Begin);
                        var ipFrom = this.binaryReader.ReadUInt32();

                        uint ipTo;
                        if (mid >= (long)this.ipv4Count - 1L)
                        {
                            ipTo = uint.MaxValue;
                        }
                        else
                        {
                            var ipToOffset = (this.baseAddress - 1) + ((mid + 1) * rowSize);
                            this.fileStream.Seek(ipToOffset, SeekOrigin.Begin);
                            ipTo = this.binaryReader.ReadUInt32();
                        }

                        if (ipNum >= ipFrom && (mid >= (long)this.ipv4Count - 1L ? ipNum <= ipTo : ipNum < ipTo))
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
                    Span<byte> ipBytes = stackalloc byte[16];
                    if (!parsedIp.TryWriteBytes(ipBytes, out _))
                    {
                        return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
                    }

                    var ipNum = new BigInteger(ipBytes, isUnsigned: true, isBigEndian: true);

                    var low = 0L;
                    var high = (long)this.ipv6Count - 1L;
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

                        BigInteger ipTo;
                        if (mid >= (long)this.ipv6Count - 1L)
                        {
                            ipTo = IPv6MaxLimit;
                        }
                        else
                        {
                            var ipToOffset = (this.baseAddressIPv6 - 1) + ((mid + 1) * rowSize);
                            this.fileStream.Seek(ipToOffset, SeekOrigin.Begin);
                            var toBytes = this.binaryReader.ReadBytes(16);
                            if (toBytes.Length < 16)
                            {
                                break;
                            }

                            ipTo = new BigInteger(toBytes, isUnsigned: true, isBigEndian: false);
                        }

                        if (ipNum >= ipFrom && (mid >= (long)this.ipv6Count - 1L ? ipNum <= ipTo : ipNum < ipTo))
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

    // Note: Mutates shared FileStream/BinaryReader state; caller must hold this.@lock.
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
