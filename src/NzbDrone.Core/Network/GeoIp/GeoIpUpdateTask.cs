// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.GeoIp;

public class GeoIpUpdateCommand : Command
{
}

public interface IGeoIpUpdateTask
{
    Task<bool> ExecuteAsync(CancellationToken cancellationToken = default);

    void StartLoop();
}

public class GeoIpUpdateTask : IGeoIpUpdateTask, IHandle<ApplicationStartedEvent>, IExecute<GeoIpUpdateCommand>, IDisposable
{
    private const string DefaultGeoLite2CityUrl = "https://raw.githubusercontent.com/P3TERX/GeoLite.mmdb/download/GeoLite2-City.mmdb";

    private readonly IConfigService configService;
    private readonly IDiskProvider diskProvider;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IEnumerable<IGeoIpProvider> geoIpProviders;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger;
    private readonly CancellationTokenSource cts = new();
    private Task loopTask;

    public GeoIpUpdateTask(
        IConfigService configService,
        IDiskProvider diskProvider,
        IAppFolderInfo appFolderInfo,
        IEnumerable<IGeoIpProvider> geoIpProviders,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        this.diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
        this.appFolderInfo = appFolderInfo ?? throw new ArgumentNullException(nameof(appFolderInfo));
        this.geoIpProviders = geoIpProviders ?? Enumerable.Empty<IGeoIpProvider>();
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        this.StartLoop();
    }

    public void Execute(GeoIpUpdateCommand message)
    {
        this.ExecuteAsync().GetAwaiter().GetResult();
    }

    public void StartLoop()
    {
        if (this.loopTask == null)
        {
            this.loopTask = Task.Run(this.RunUpdateLoopAsync, this.cts.Token);
        }
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            this.logger.Info("Executing MaxMind GeoIP database update task...");

            var downloadUrl = this.ResolveDownloadUrl();
            this.logger.Info("Downloading GeoIP database archive from '{0}'...", downloadUrl);

            var downloadedBytes = await this.safeHttpClientService.DownloadBytesAsync(
                downloadUrl,
                150 * 1024 * 1024,
                cancellationToken).ConfigureAwait(false);

            if (downloadedBytes == null || downloadedBytes.Length == 0)
            {
                this.logger.Warn("GeoIP download returned empty payload from '{0}'.", downloadUrl);
                return false;
            }

            var mmdbBytes = ExtractMmdbBytes(downloadedBytes);
            if (mmdbBytes == null || mmdbBytes.Length == 0)
            {
                this.logger.Error("Failed to extract valid .mmdb file from downloaded payload ({0} bytes).", downloadedBytes.Length);
                return false;
            }

            var targetPath = this.ResolveTargetPath();
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                this.diskProvider.EnsureFolder(targetDir);
            }

            var tempFilePath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(tempFilePath, mmdbBytes, cancellationToken).ConfigureAwait(false);

                // Atomic swap
                File.Move(tempFilePath, targetPath, overwrite: true);
                this.logger.Info("Successfully installed updated MaxMind GeoIP database at '{0}' ({1} bytes).", targetPath, mmdbBytes.Length);

                // Invalidate/reload active MaxMind providers
                foreach (var provider in this.geoIpProviders.OfType<MaxMindGeoIpProvider>())
                {
                    provider.Reload();
                }

                return true;
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    try
                    {
                        File.Delete(tempFilePath);
                    }
                    catch
                    {
                        // Ignore cleanup failure of temporary file
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            this.logger.Debug("GeoIP update task was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to execute GeoIP update task.");
            return false;
        }
    }

    public static byte[] ExtractMmdbBytes(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        // 1. Check GZip magic header (0x1f, 0x8b)
        if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            using var decompressedStream = new MemoryStream();
            using (var gzStream = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            {
                gzStream.CopyTo(decompressedStream);
            }

            var decompressedBytes = decompressedStream.ToArray();

            // Check if decompressed stream contains a TAR archive
            try
            {
                decompressedStream.Position = 0;
                using var tarReader = new TarReader(decompressedStream);
                while (tarReader.GetNextEntry() is { } entry)
                {
                    if (entry.Name.EndsWith(".mmdb", StringComparison.OrdinalIgnoreCase) && entry.DataStream != null)
                    {
                        using var ms = new MemoryStream();
                        entry.DataStream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                // Not a tar archive; check if decompressed payload itself is .mmdb
            }

            if (decompressedBytes.Length > 0 && IsMmdbContent(decompressedBytes))
            {
                return decompressedBytes;
            }
        }

        // 2. Check ZIP archive (PK..)
        if (data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B && (data[2] == 0x03 || data[2] == 0x05 || data[2] == 0x07))
        {
            try
            {
                using var memStream = new MemoryStream(data);
                using var archive = new ZipArchive(memStream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".mmdb", StringComparison.OrdinalIgnoreCase))
                    {
                        using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        entryStream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                // Failed zip parsing
            }
        }

        // 3. Raw .mmdb
        if (IsMmdbContent(data))
        {
            return data;
        }

        return null;
    }

    private static bool IsMmdbContent(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 16)
        {
            return false;
        }

        var marker = "\xAB\xCD\xEFMaxMind.Db"u8.ToArray();
        return bytes.AsSpan().IndexOf(marker) >= 0 || bytes.Length > 1024;
    }

    private string ResolveDownloadUrl()
    {
        var configuredUrl = this.configService.GetValue("GeoIpDownloadUrl", string.Empty);
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl;
        }

        var licenseKey = this.configService.GetValue("MaxMindLicenseKey", string.Empty);
        if (!string.IsNullOrWhiteSpace(licenseKey))
        {
            return $"https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-City&license_key={licenseKey}&suffix=tar.gz";
        }

        return DefaultGeoLite2CityUrl;
    }

    private string ResolveTargetPath()
    {
        var configuredPath = this.configService.GetValue("GeoLite2DatabasePath", string.Empty);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return Path.Combine(this.appFolderInfo.AppDataFolder, "GeoIP", "GeoLite2-City.mmdb");
    }

    private async Task RunUpdateLoopAsync()
    {
        // Monthly schedule check (default 30 days)
        while (!this.cts.Token.IsCancellationRequested)
        {
            try
            {
                var days = Math.Max(1, this.configService.GetValueInt("GeoIpUpdateIntervalDays", 30));
                await Task.Delay(TimeSpan.FromDays(days), this.cts.Token).ConfigureAwait(false);
                await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error occurred during monthly GeoIP update loop.");
            }
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }
}
