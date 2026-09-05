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

        var rules = new List<string>();

        // 1. Ingest from local file path if configured
        if (!string.IsNullOrWhiteSpace(this.configService.BlocklistPath))
        {
            try
            {
                if (File.Exists(this.configService.BlocklistPath))
                {
                    var fileBytes = await File.ReadAllBytesAsync(this.configService.BlocklistPath, cancellationToken);
                    var fileLines = ParseLines(fileBytes);
                    rules.AddRange(fileLines);
                    this.logger.Info("Read {0} raw blocklist lines from local path '{1}'.", fileLines.Count, this.configService.BlocklistPath);
                }
                else
                {
                    this.logger.Warn("Configured blocklist path does not exist: '{0}'", this.configService.BlocklistPath);
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
                    var urlLines = ParseLines(urlBytes);
                    rules.AddRange(urlLines);
                    this.logger.Info("Downloaded {0} raw blocklist lines from URL '{1}'.", urlLines.Count, this.configService.BlocklistUrl);
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to download blocklist feed from '{0}'", this.configService.BlocklistUrl);
            }
        }

        if (rules.Count > 0)
        {
            var loaded = await this.blocklistService.LoadRulesAsync(rules);
            this.logger.Info("Blocklist rules refreshed: {0} rules loaded into active provider.", loaded);
            return loaded;
        }

        this.logger.Warn("No blocklist rules could be ingested from configured sources.");
        return 0;
    }

    private static List<string> ParseLines(byte[] data)
    {
        var lines = new List<string>();
        if (data == null || data.Length == 0)
        {
            return lines;
        }

        Stream stream = new MemoryStream(data);
        if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            stream = new GZipStream(stream, CompressionMode.Decompress);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line.Trim());
            }
        }

        return lines;
    }
}
