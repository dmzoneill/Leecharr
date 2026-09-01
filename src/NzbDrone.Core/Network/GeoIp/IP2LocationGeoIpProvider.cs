using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.GeoIp;

public class IP2LocationGeoIpProvider : IGeoIpProvider, IDisposable
{
    private readonly IDiskProvider _diskProvider;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;
    private readonly object _lock = new();

    private FileStream _fileStream;
    private BinaryReader _binaryReader;
    private string _resolvedDatabasePath;
    private byte _dbType;
    private byte _dbColumn;
    private uint _ipv4Count;
    private uint _baseAddress;
    private uint _ipv6Count;
    private uint _baseAddressIPv6;
    private bool _disposed;

    public string ProviderId => "IP2Location";
    public string DisplayName => "IP2Location Binary (.BIN)";
    public string Version => "1.0";
    public bool IsAvailable => !string.IsNullOrEmpty(GetDatabasePath());
    public GeoIpCapabilities Capabilities => GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.OfflineDatabase;

    public IP2LocationGeoIpProvider(IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
    {
        _diskProvider = diskProvider;
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public string GetDatabasePath()
    {
        var candidates = new List<string>
        {
            "/config/GeoIP/IP2LOCATION-LITE-DB1.BIN",
            "/config/GeoIP/IP2LOCATION-LITE-DB11.BIN",
            "/config/GeoIP/IP2LOCATION-LITE-DB3.BIN",
            "/config/GeoIP/IP2Location.BIN",
            "/config/IP2Location.BIN",
            Path.Combine(_appFolderInfo.AppDataFolder, "GeoIP", "IP2LOCATION-LITE-DB1.BIN"),
            Path.Combine(_appFolderInfo.AppDataFolder, "GeoIP", "IP2Location.BIN"),
            Path.Combine(_appFolderInfo.AppDataFolder, "IP2Location.BIN"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IP2Location.BIN")
        };

        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && _diskProvider.FileExists(path))
            {
                return path;
            }
        }

        return null;
    }

    public Task<GeoIpHealthResult> ProbeHealthAsync()
    {
        var dbPath = GetDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = "IP2Location .BIN database not found. Place the file in /config/GeoIP/ or AppData.",
                Warnings = new List<string> { "Database file missing." }
            });
        }

        try
        {
            EnsureReader(dbPath);
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = true,
                StatusMessage = $"IP2Location database loaded successfully from {dbPath} (DB Type: {_dbType}, IPv4 Records: {_ipv4Count})."
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Failed to read IP2Location database: {ex.Message}",
                Warnings = new List<string> { ex.Message }
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

        var dbPath = GetDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
        }

        try
        {
            lock (_lock)
            {
                EnsureReader(dbPath);
                if (_binaryReader == null)
                {
                    return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
                }

                if (parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var ipBytes = parsedIp.GetAddressBytes();
                    Array.Reverse(ipBytes);
                    var ipNum = BitConverter.ToUInt32(ipBytes, 0);

                    var low = 0L;
                    var high = (long)_ipv4Count;
                    var rowSize = (long)_dbColumn * 4;

                    while (low <= high)
                    {
                        var mid = low + ((high - low) / 2);
                        var rowOffset = (_baseAddress - 1) + (mid * rowSize);

                        _fileStream.Seek(rowOffset, SeekOrigin.Begin);
                        var ipFrom = _binaryReader.ReadUInt32();

                        var ipToOffset = (_baseAddress - 1) + ((mid + 1) * rowSize);
                        _fileStream.Seek(ipToOffset, SeekOrigin.Begin);
                        var ipTo = _binaryReader.ReadUInt32();

                        if (ipNum >= ipFrom && ipNum < ipTo)
                        {
                            var result = ReadRecordData(rowOffset);
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
            _logger.Warn(ex, "Error performing IP2Location lookup for IP: {0}", ipAddress);
        }

        return Task.FromResult(new GeoLocationInfo { IpAddress = ipAddress });
    }

    private GeoLocationInfo ReadRecordData(long rowOffset)
    {
        var info = new GeoLocationInfo();
        _fileStream.Seek(rowOffset + 4, SeekOrigin.Begin);

        var countryOffset = _binaryReader.ReadUInt32();
        if (countryOffset > 0)
        {
            _fileStream.Seek(countryOffset, SeekOrigin.Begin);
            var len = _binaryReader.ReadByte();
            var countryCodeBytes = _binaryReader.ReadBytes(len);
            info.CountryCode = Encoding.ASCII.GetString(countryCodeBytes).Trim();

            var nameLen = _binaryReader.ReadByte();
            var countryNameBytes = _binaryReader.ReadBytes(nameLen);
            info.CountryName = Encoding.ASCII.GetString(countryNameBytes).Trim();
        }

        return info;
    }

    private void EnsureReader(string dbPath)
    {
        if (_binaryReader != null && _resolvedDatabasePath == dbPath)
        {
            return;
        }

        CloseReader();

        _fileStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _binaryReader = new BinaryReader(_fileStream);
        _resolvedDatabasePath = dbPath;

        _dbType = _binaryReader.ReadByte();
        _dbColumn = _binaryReader.ReadByte();
        var year = _binaryReader.ReadByte();
        var month = _binaryReader.ReadByte();
        var day = _binaryReader.ReadByte();
        _ipv4Count = _binaryReader.ReadUInt32();
        _baseAddress = _binaryReader.ReadUInt32();
        _ipv6Count = _binaryReader.ReadUInt32();
        _baseAddressIPv6 = _binaryReader.ReadUInt32();
    }

    private void CloseReader()
    {
        _binaryReader?.Dispose();
        _binaryReader = null;
        _fileStream?.Dispose();
        _fileStream = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            lock (_lock)
            {
                CloseReader();
            }
        }
    }
}
