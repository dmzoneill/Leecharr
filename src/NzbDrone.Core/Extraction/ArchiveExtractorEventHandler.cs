// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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

public class ArchiveExtractionFailedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string ArchivePath { get; set; }

    public string DestinationDirectory { get; set; }

    public string ErrorMessage { get; set; }
}

public class ArchiveExtractorEventHandler : IHandle<TorrentDownloadCompletedEvent>
{
    private readonly IArchiveExtractorService extractorService;
    private readonly ITorrentFileService torrentFileService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly IConfigService configService;
    private readonly SemaphoreSlim extractionSemaphore;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public SemaphoreSlim ConcurrencySemaphore => this.extractionSemaphore;

    public int ConcurrencyLimit { get; }

    public ArchiveExtractorEventHandler(
        IArchiveExtractorService extractorService,
        ITorrentFileService torrentFileService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator,
        IConfigService configService = null,
        int? maxConcurrentExtractions = null)
    {
        this.extractorService = extractorService;
        this.torrentFileService = torrentFileService;
        this.diskProvider = diskProvider;
        this.eventAggregator = eventAggregator;
        this.configService = configService;

        var concurrency = maxConcurrentExtractions
            ?? (configService != null && configService.MaxConcurrentExtractions > 0 ? configService.MaxConcurrentExtractions : 2);
        this.ConcurrencyLimit = Math.Max(1, concurrency);
        this.extractionSemaphore = new SemaphoreSlim(this.ConcurrencyLimit, this.ConcurrencyLimit);
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        _ = this.HandleAsync(message);
    }

    public async Task HandleAsync(TorrentDownloadCompletedEvent message)
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

        try
        {
            var files = this.torrentFileService.GetFiles(message.Torrent.Id);
            var savePath = message.Torrent.SavePath;
            if (string.IsNullOrEmpty(savePath))
            {
                return;
            }

            var isSingleFile = this.diskProvider.FileExists(savePath);
            var rootDir = isSingleFile
                ? Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? savePath
                : savePath;

            var password = this.configService?.GetValue("ArchivePassword", (string)null);
            var passwordsStr = this.configService?.GetValue("ArchivePasswords", (string)null);
            IReadOnlyList<string> candidatePasswords = null;
            if (!string.IsNullOrWhiteSpace(passwordsStr))
            {
                candidatePasswords = passwordsStr.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            foreach (var file in files)
            {
                if (this.extractorService.IsArchiveFile(file.Path) && !IsSecondaryVolume(file.Path))
                {
                    var fullPath = isSingleFile && string.Equals(Path.GetFileName(savePath), file.Path, StringComparison.OrdinalIgnoreCase)
                        ? savePath
                        : Path.Combine(savePath, file.Path);

                    if (!TorrentPathValidator.IsStrictSubPath(rootDir, fullPath))
                    {
                        this.logger.Warn("Refusing to auto-extract archive with path traversal outside rootDir for torrent {0}: {1}", message.Torrent.Name, file.Path);
                        continue;
                    }

                    if (this.diskProvider.FileExists(fullPath))
                    {
                        var destDir = Path.GetDirectoryName(fullPath) ?? rootDir;
                        if (this.IsArchiveAlreadyExtracted(destDir, fullPath))
                        {
                            this.logger.Debug("Archive {0} has already been extracted (receipt found). Skipping auto-extraction for torrent {1}.", fullPath, message.Torrent.Name);
                            continue;
                        }

                        var estimatedSize = file.Size > 0 ? file.Size : this.diskProvider.GetFileSize(fullPath);
                        var basePrefix = GetArchiveBasePrefix(file.Path);
                        var relatedFiles = files.Where(f =>
                        {
                            var fn = Path.GetFileName(f.Path);
                            return !string.IsNullOrEmpty(basePrefix) && fn.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase);
                        }).ToList();

                        if (relatedFiles.Count > 1)
                        {
                            var sum = relatedFiles.Sum(f => f.Size > 0 ? f.Size : 0);
                            if (sum > 0)
                            {
                                estimatedSize = sum;
                            }
                        }

                        if (estimatedSize <= 0)
                        {
                            estimatedSize = ArchiveTimeoutCalculator.EstimateTotalArchiveSize(fullPath, this.diskProvider);
                        }

                        var requiredSpace = (long)(estimatedSize * 1.5);
                        var availableSpace = this.diskProvider.GetAvailableSpace(destDir);

                        if (availableSpace.HasValue && availableSpace.Value < requiredSpace)
                        {
                            this.logger.Warn(
                                "Insufficient free disk space on '{0}' for extracting '{1}'. Required: {2} bytes (1.5x estimated size), Available: {3} bytes.",
                                destDir,
                                fullPath,
                                requiredSpace,
                                availableSpace.Value);

                            this.eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent
                            {
                                Torrent = message.Torrent,
                                ArchivePath = fullPath,
                                DestinationDirectory = destDir,
                                ErrorMessage = $"Insufficient free disk space on '{destDir}'. Required: {requiredSpace:N0} bytes, Available: {availableSpace.Value:N0} bytes.",
                            });

                            continue;
                        }

                        this.logger.Info("Queuing auto-extraction for archive {0} for completed torrent {1}", fullPath, message.Torrent.Name);
                        await this.extractionSemaphore.WaitAsync();
                        bool success;
                        try
                        {
                            this.logger.Info("Auto-extracting archive {0} for completed torrent {1}", fullPath, message.Torrent.Name);
                            success = await this.extractorService.ExtractArchiveAsync(fullPath, destDir, password, candidatePasswords);
                        }
                        finally
                        {
                            this.extractionSemaphore.Release();
                        }

                        if (success)
                        {
                            this.RecordExtractionReceipt(destDir, fullPath);

                            this.eventAggregator.PublishEvent(new ArchiveExtractionCompletedEvent
                            {
                                Torrent = message.Torrent,
                                ArchivePath = fullPath,
                                DestinationDirectory = destDir,
                            });
                        }
                        else
                        {
                            this.logger.Warn("Auto-extraction failed for archive {0} in torrent {1}", fullPath, message.Torrent.Name);
                            this.eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent
                            {
                                Torrent = message.Torrent,
                                ArchivePath = fullPath,
                                DestinationDirectory = destDir,
                                ErrorMessage = $"Extraction failed for archive '{fullPath}' to destination '{destDir}'.",
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to auto-extract archives for torrent {0}", message.Torrent.Name);
            this.eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent
            {
                Torrent = message.Torrent,
                ArchivePath = message.Torrent.SavePath,
                DestinationDirectory = message.Torrent.SavePath,
                ErrorMessage = $"Failed to auto-extract archives for torrent {message.Torrent.Name}: {ex.Message}",
            });
        }
    }

    public static bool IsSecondaryVolume(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (Regex.IsMatch(ext, @"^\.(r\d{2}|\d{3}|z\d{2})$", RegexOptions.IgnoreCase) && ext != ".001")
        {
            return true;
        }

        var partMatch = Regex.Match(path, @"\.part(\d+)\.(rar|7z|zip)$", RegexOptions.IgnoreCase);
        if (partMatch.Success && int.TryParse(partMatch.Groups[1].Value, out var partNum))
        {
            return partNum > 1;
        }

        var splitMatch = Regex.Match(path, @"\.(7z|tar|zip|rar)\.(\d+)$", RegexOptions.IgnoreCase);
        if (splitMatch.Success && int.TryParse(splitMatch.Groups[2].Value, out var splitNum))
        {
            return splitNum > 1;
        }

        return false;
    }

    public static string GetArchiveBasePrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(path);
        var partMatch = Regex.Match(fileName, @"^(.*?)\.part\d+\.(rar|7z|zip)$", RegexOptions.IgnoreCase);
        if (partMatch.Success)
        {
            return partMatch.Groups[1].Value;
        }

        var numMatch = Regex.Match(fileName, @"^(.*?)\.(r\d{2}|\d{3}|z\d{2}|001|rar|zip|7z)$", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            return numMatch.Groups[1].Value;
        }

        var splitMatch = Regex.Match(fileName, @"^(.*?)\.(7z|tar|zip|rar)\.\d+$", RegexOptions.IgnoreCase);
        if (splitMatch.Success)
        {
            return splitMatch.Groups[1].Value;
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    private bool IsArchiveAlreadyExtracted(string destinationDirectory, string archiveFilePath)
    {
        try
        {
            var fileName = Path.GetFileName(archiveFilePath);
            var receiptPath = Path.Combine(destinationDirectory, $".leecharr_extracted_{fileName}");
            if (this.diskProvider.FileExists(receiptPath))
            {
                return true;
            }

            var globalReceipt = Path.Combine(destinationDirectory, ".leecharr_extracted");
            if (this.diskProvider.FileExists(globalReceipt))
            {
                var text = this.diskProvider.ReadAllText(globalReceipt);
                if (!string.IsNullOrEmpty(text) && text.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error checking extraction receipt for {0}", archiveFilePath);
        }

        return false;
    }

    private void RecordExtractionReceipt(string destinationDirectory, string archiveFilePath)
    {
        try
        {
            var fileName = Path.GetFileName(archiveFilePath);
            var receiptPath = Path.Combine(destinationDirectory, $".leecharr_extracted_{fileName}");
            var content = $"Extracted: {DateTime.UtcNow:o}\nArchive: {fileName}\n";
            this.diskProvider.WriteAllText(receiptPath, content);

            var globalReceipt = Path.Combine(destinationDirectory, ".leecharr_extracted");
            if (this.diskProvider.FileExists(globalReceipt))
            {
                var existing = this.diskProvider.ReadAllText(globalReceipt);
                if (!existing.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    this.diskProvider.WriteAllText(globalReceipt, existing + fileName + Environment.NewLine);
                }
            }
            else
            {
                this.diskProvider.WriteAllText(globalReceipt, fileName + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to write extraction receipt for archive {0}", archiveFilePath);
        }
    }
}
