// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractionCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string ArchivePath { get; set; }

    public string DestinationDirectory { get; set; }
}

public class ArchiveExtractorEventHandler : IHandle<TorrentDownloadCompletedEvent>
{
    private readonly IArchiveExtractorService extractorService;
    private readonly ITorrentFileService torrentFileService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly IConfigService configService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public ArchiveExtractorEventHandler(
        IArchiveExtractorService extractorService,
        ITorrentFileService torrentFileService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator,
        IConfigService configService = null)
    {
        this.extractorService = extractorService;
        this.torrentFileService = torrentFileService;
        this.diskProvider = diskProvider;
        this.eventAggregator = eventAggregator;
        this.configService = configService;
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (this.configService != null && !this.configService.AutoExtractArchives && !this.configService.GetValueBoolean("AutoExtract", false) && !this.configService.GetValueBoolean("AutoExtractEnabled", false))
        {
            this.logger.Debug("Archive auto-extraction is disabled in configuration. Skipping extraction for torrent {0}", message.Torrent.Name);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var files = this.torrentFileService.GetFiles(message.Torrent.Id);
                var savePath = message.Torrent.SavePath;
                if (string.IsNullOrEmpty(savePath))
                {
                    return;
                }

                foreach (var file in files)
                {
                    if (this.extractorService.IsArchiveFile(file.Path) && !IsSecondaryVolume(file.Path))
                    {
                        var fullPath = Path.Combine(savePath, file.Path);
                        if (this.diskProvider.FileExists(fullPath))
                        {
                            this.logger.Info("Auto-extracting archive {0} for completed torrent {1}", fullPath, message.Torrent.Name);
                            var destDir = Path.GetDirectoryName(fullPath);
                            var success = await this.extractorService.ExtractArchiveAsync(fullPath, destDir);
                            if (success)
                            {
                                this.eventAggregator.PublishEvent(new ArchiveExtractionCompletedEvent
                                {
                                    Torrent = message.Torrent,
                                    ArchivePath = fullPath,
                                    DestinationDirectory = destDir,
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to auto-extract archives for torrent {0}", message.Torrent.Name);
            }
        });
    }

    public static bool IsSecondaryVolume(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (Regex.IsMatch(ext, @"^\.(r\d{2}|\d{3}|z\d{2})$", RegexOptions.IgnoreCase) && ext != ".r00" && ext != ".001" && ext != ".z01")
        {
            return true;
        }

        var partMatch = Regex.Match(path, @"\.part(\d+)\.(rar|7z|zip)$", RegexOptions.IgnoreCase);
        if (partMatch.Success && int.TryParse(partMatch.Groups[1].Value, out var partNum))
        {
            return partNum > 1;
        }

        var splitMatch = Regex.Match(path, @"\.(7z|tar|zip)\.(\d+)$", RegexOptions.IgnoreCase);
        if (splitMatch.Success && int.TryParse(splitMatch.Groups[2].Value, out var splitNum))
        {
            return splitNum > 1;
        }

        return false;
    }
}
