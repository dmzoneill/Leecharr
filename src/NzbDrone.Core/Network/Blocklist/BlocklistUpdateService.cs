// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.Network.Blocklist;

public class BlocklistUpdateService : IBlocklistUpdateService
{
    private readonly IBlocklistService blocklistService;
    private readonly IConfigService configService;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger;

    public BlocklistUpdateService(
        IBlocklistService blocklistService,
        IConfigService configService,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.blocklistService = blocklistService ?? throw new ArgumentNullException(nameof(blocklistService));
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<int> UpdateRulesAsync(CancellationToken cancellationToken = default)
    {
        if (!this.configService.BlocklistEnabled)
        {
            this.logger.Debug("Blocklist update skipped: blocklist is disabled in configuration.");
            return 0;
        }

        var sources = new List<Func<IEnumerable<string>>>();

        // 1. Ingest from local file path if configured
        if (!string.IsNullOrWhiteSpace(this.configService.BlocklistPath))
        {
            try
            {
                var filePath = this.configService.BlocklistPath;
                if (File.Exists(filePath))
                {
                    sources.Add(() => StreamFileLines(filePath));
                    this.logger.Info("Configured local blocklist file source '{0}'.", filePath);
                }
                else
                {
                    this.logger.Warn("Configured blocklist path does not exist: '{0}'", filePath);
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to read blocklist file from '{0}'", this.configService.BlocklistPath);
            }
        }

        // 2. Ingest from remote URL if configured
        if (!string.IsNullOrWhiteSpace(this.configService.BlocklistUrl))
        {
            try
            {
                var urlBytes = await this.safeHttpClientService.DownloadBytesAsync(
                    this.configService.BlocklistUrl,
                    50 * 1024 * 1024,
                    cancellationToken);

                if (urlBytes != null && urlBytes.Length > 0)
                {
                    sources.Add(() => ParseLines(urlBytes));
                    this.logger.Info("Downloaded {0} bytes from blocklist URL '{1}'.", urlBytes.Length, this.configService.BlocklistUrl);
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to download blocklist feed from '{0}'", this.configService.BlocklistUrl);
            }
        }

        if (sources.Count > 0)
        {
            IEnumerable<string> StreamAllRules()
            {
                foreach (var source in sources)
                {
                    foreach (var line in source())
                    {
                        yield return line;
                    }
                }
            }

            var loaded = await this.blocklistService.LoadRulesAsync(StreamAllRules());
            if (loaded > 0)
            {
                this.logger.Info("Blocklist rules refreshed: {0} rules loaded into active provider.", loaded);
                return loaded;
            }
        }

        this.logger.Warn("No blocklist rules could be ingested from configured sources.");
        return 0;
    }

    public static IEnumerable<string> ParseLines(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            yield break;
        }

        using var memStream = new MemoryStream(data, writable: false);
        foreach (var line in ParseLines(memStream))
        {
            yield return line;
        }
    }

    public static IEnumerable<string> ParseLines(Stream stream)
    {
        if (stream == null)
        {
            yield break;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var header = new byte[4];
        var bytesRead = stream.Read(header, 0, 4);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        // 1. Check GZip magic header (0x1f 0x8b)
        if (bytesRead >= 2 && header[0] == 0x1f && header[1] == 0x8b)
        {
            using var gzipStream = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(gzipStream, Encoding.UTF8);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    yield return line.Trim();
                }
            }

            yield break;
        }

        // 2. Check ZIP archive magic header (0x50 0x4B 0x03 0x04)
        if (bytesRead >= 4 && header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07))
        {
            ZipArchive archive = null;
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch
            {
                archive = null;
            }

            if (archive != null)
            {
                using (archive)
                {
                    foreach (var entry in archive.Entries)
                    {
                        using var entryStream = entry.Open();
                        using var reader = new StreamReader(entryStream, Encoding.UTF8);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                yield return line.Trim();
                            }
                        }
                    }
                }

                yield break;
            }
        }

        // 3. Fallback to raw text
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var rawReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        string rawLine;
        while ((rawLine = rawReader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(rawLine))
            {
                yield return rawLine.Trim();
            }
        }
    }

    private static IEnumerable<string> StreamFileLines(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        foreach (var line in ParseLines(stream))
        {
            yield return line;
        }
    }
}
