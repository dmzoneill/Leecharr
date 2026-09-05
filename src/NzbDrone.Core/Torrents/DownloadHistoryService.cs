// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryService : IDownloadHistoryService, IHandle<TorrentAddedEvent>, IHandle<TorrentDeletedEvent>
{
    private readonly IDownloadHistoryRepository historyRepository;
    private readonly ITorrentRepository torrentRepository;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly ICategoryService categoryService;
    private readonly IStoragePathService storagePathService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ITorrentFileRepository fileRepository;
    private readonly IConfigService configService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly ITorrentMediaMetadataRepository mediaMetadataRepository;
    private readonly Logger logger;

    public DownloadHistoryService(
        IDownloadHistoryRepository historyRepository,
        ITorrentRepository torrentRepository,
        ITrackerEntryRepository trackerEntryRepository,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator,
        ISafeHttpClientService safeHttpClientService = null,
        ICategoryService categoryService = null,
        IStoragePathService storagePathService = null,
        ITorrentFileParser torrentFileParser = null,
        ITorrentFileRepository fileRepository = null,
        IConfigService configService = null,
        IAppFolderInfo appFolderInfo = null,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null)
    {
        this.historyRepository = historyRepository;
        this.torrentRepository = torrentRepository;
        this.trackerEntryRepository = trackerEntryRepository;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.categoryService = categoryService;
        this.storagePathService = storagePathService;
        this.torrentFileParser = torrentFileParser ?? new TorrentFileParser();
        this.fileRepository = fileRepository;
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
        this.mediaMetadataRepository = mediaMetadataRepository;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public List<DownloadHistory> GetAll(string query = null, string status = null, int limit = 500)
    {
        return this.historyRepository.GetHistory(query, status, limit);
    }

    public DownloadHistory Get(int id)
    {
        return this.historyRepository.Get(id);
    }

    public DownloadHistory GetByInfoHash(string infoHash)
    {
        return this.historyRepository.FindByInfoHash(infoHash);
    }

    public void Delete(int id)
    {
        this.logger.Info("Deleting download history entry {0}", id);
        this.historyRepository.Delete(id);
    }

    public void ClearAll()
    {
        this.logger.Info("Clearing all download history entries");
        this.historyRepository.DeleteAll();
    }

    public DownloadHistory RecordTorrentAdded(
        Torrent torrent,
        string source = null,
        string magnetUrl = null,
        string downloadUrl = null,
        string indexerName = null)
    {
        if (torrent == null)
        {
            return null;
        }

        var existing = !string.IsNullOrEmpty(torrent.InfoHash)
            ? this.historyRepository.FindByInfoHash(torrent.InfoHash)
            : null;

        if (existing != null)
        {
            existing.TorrentId = torrent.Id;
            existing.Title = torrent.Name ?? existing.Title;
            existing.TotalSize = torrent.TotalSize > 0 ? torrent.TotalSize : existing.TotalSize;
            existing.Status = "Active";
            existing.DateRemoved = null;

            if (!string.IsNullOrWhiteSpace(source))
            {
                existing.Source = source;
            }

            if (!string.IsNullOrWhiteSpace(magnetUrl))
            {
                existing.MagnetUrl = magnetUrl;
            }

            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                existing.DownloadUrl = downloadUrl;
            }

            if (!string.IsNullOrWhiteSpace(indexerName))
            {
                existing.IndexerName = indexerName;
            }

            if (string.IsNullOrWhiteSpace(existing.PrimaryTracker))
            {
                existing.PrimaryTracker = this.trackerEntryRepository?.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;
            }

            this.historyRepository.Update(existing);
            return existing;
        }

        var tracker = this.trackerEntryRepository.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;

        var entry = new DownloadHistory
        {
            TorrentId = torrent.Id,
            Title = torrent.Name ?? "Unknown Release",
            InfoHash = torrent.InfoHash ?? string.Empty,
            TotalSize = torrent.TotalSize,
            DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
            DateCompleted = torrent.Progress >= 1.0 ? DateTime.UtcNow : null,
            DateRemoved = null,
            Uploaded = torrent.Uploaded,
            Downloaded = torrent.Downloaded,
            Ratio = torrent.Ratio,
            SeedingTime = 0,
            PrimaryTracker = tracker,
            IndexerName = indexerName,
            Source = source ?? (torrent.Category ?? "Manual"),
            MagnetUrl = magnetUrl,
            DownloadUrl = downloadUrl,
            Status = "Active",
        };

        return this.historyRepository.Insert(entry);
    }

    public void RecordTorrentUpdated(Torrent torrent)
    {
        if (torrent == null)
        {
            return;
        }

        var entry = this.historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? this.historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry == null)
        {
            return;
        }

        entry.Uploaded = torrent.Uploaded;
        entry.Downloaded = torrent.Downloaded;
        entry.Ratio = torrent.Ratio;
        if (torrent.Progress >= 1.0 && entry.DateCompleted == null)
        {
            entry.DateCompleted = DateTime.UtcNow;
            entry.Status = "Completed";
        }

        this.historyRepository.Update(entry);
    }

    public void RecordTorrentRemoved(Torrent torrent, string reason = "Deleted from library")
    {
        if (torrent == null)
        {
            return;
        }

        var entry = this.historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? this.historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry == null)
        {
            var tracker = this.trackerEntryRepository.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;

            entry = new DownloadHistory
            {
                Title = torrent.Name ?? "Unknown Release",
                InfoHash = torrent.InfoHash ?? string.Empty,
                TotalSize = torrent.TotalSize,
                DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
                DateCompleted = torrent.Progress >= 1.0 ? DateTime.UtcNow : null,
                DateRemoved = DateTime.UtcNow,
                Uploaded = torrent.Uploaded,
                Downloaded = torrent.Downloaded,
                Ratio = torrent.Ratio,
                SeedingTime = torrent.DateAdded != default ? (long)(DateTime.UtcNow - torrent.DateAdded).TotalSeconds : 0,
                PrimaryTracker = tracker,
                Source = "Library",
                Status = "Removed",
                RemovalReason = reason,
            };

            if (this.mediaMetadataRepository != null)
            {
                var metadata = this.mediaMetadataRepository.GetByTorrentId(torrent.Id);
                if (metadata != null)
                {
                    entry.DataJson = JsonSerializer.Serialize(metadata);
                }
            }

            this.historyRepository.Insert(entry);
            return;
        }

        if (this.mediaMetadataRepository != null)
        {
            var metadata = (entry.TorrentId.HasValue ? this.mediaMetadataRepository.GetByTorrentId(entry.TorrentId.Value) : null)
                ?? (torrent.Id > 0 ? this.mediaMetadataRepository.GetByTorrentId(torrent.Id) : null);

            if (metadata != null)
            {
                entry.DataJson = JsonSerializer.Serialize(metadata);
            }
        }

        entry.TorrentId = null;
        entry.DateRemoved = DateTime.UtcNow;
        entry.Uploaded = torrent.Uploaded;
        entry.Downloaded = torrent.Downloaded;
        entry.Ratio = torrent.Ratio;
        entry.Status = "Removed";
        entry.RemovalReason = reason;

        this.historyRepository.Update(entry);
    }

    public Torrent ReAdd(int historyId)
    {
        return this.ReAddAsync(historyId).GetAwaiter().GetResult();
    }

    public async Task<Torrent> ReAddAsync(int historyId)
    {
        var entry = this.historyRepository.Get(historyId);
        if (entry == null)
        {
            throw new ArgumentException($"History entry {historyId} not found");
        }

        if (this.torrentRepository.ExistsByInfoHash(entry.InfoHash))
        {
            throw new InvalidOperationException($"Torrent with info hash {entry.InfoHash} is already in the active library");
        }

        // 1. Resolve effective category - do not blindly assign entry.Source (which is tracker/indexer attribution)
        string effectiveCategory = null;
        if (!string.IsNullOrWhiteSpace(entry.Source) && this.categoryService != null)
        {
            var matchedCat = this.categoryService.GetByName(entry.Source);
            if (matchedCat != null && string.Equals(matchedCat.Name, entry.Source, StringComparison.OrdinalIgnoreCase))
            {
                effectiveCategory = matchedCat.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(effectiveCategory))
        {
            effectiveCategory = this.configService?.DefaultCategory;
        }

        // 2. Resolve SavePath
        var defaultDownloadDir = this.configService?.DownloadDir ?? "/downloads";
        var effectiveSavePath = this.categoryService != null
            ? this.categoryService.GetSavePathForCategory(effectiveCategory, defaultDownloadDir)
            : defaultDownloadDir;

        if (string.IsNullOrWhiteSpace(effectiveSavePath))
        {
            effectiveSavePath = defaultDownloadDir;
        }

        byte[] torrentBytes = null;
        var magnetUri = entry.MagnetUrl;

        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            if (!string.IsNullOrWhiteSpace(entry.DownloadUrl))
            {
                if (entry.DownloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    magnetUri = entry.DownloadUrl;
                }
                else
                {
                    try
                    {
                        torrentBytes = await this.safeHttpClientService.DownloadBytesAsync(entry.DownloadUrl);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to download .torrent from {0} for re-added historical torrent with info hash {1}", entry.DownloadUrl, entry.InfoHash);
                    }
                }
            }

            if ((torrentBytes == null || torrentBytes.Length == 0) && string.IsNullOrWhiteSpace(magnetUri) && !string.IsNullOrWhiteSpace(entry.InfoHash))
            {
                try
                {
                    var hash = entry.InfoHash.ToLowerInvariant();
                    var pathsToTry = new List<string>();

                    if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
                    {
                        pathsToTry.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Torrents", $"{hash}.torrent"));
                        pathsToTry.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Leecharr", "Torrents", $"{hash}.torrent"));
                    }

                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (!string.IsNullOrWhiteSpace(appData))
                    {
                        pathsToTry.Add(Path.Combine(appData, "Torrents", $"{hash}.torrent"));
                        pathsToTry.Add(Path.Combine(appData, "Leecharr", "Torrents", $"{hash}.torrent"));
                    }

                    foreach (var path in pathsToTry)
                    {
                        if (File.Exists(path))
                        {
                            torrentBytes = await File.ReadAllBytesAsync(path);
                            if (torrentBytes != null && torrentBytes.Length > 0)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to read cached .torrent file for {0}", entry.InfoHash);
                }
            }

            if ((torrentBytes == null || torrentBytes.Length == 0) && string.IsNullOrWhiteSpace(magnetUri))
            {
                if (!string.IsNullOrWhiteSpace(entry.InfoHash))
                {
                    var magnetBuilder = new StringBuilder($"magnet:?xt=urn:btih:{entry.InfoHash}");
                    if (!string.IsNullOrWhiteSpace(entry.Title))
                    {
                        magnetBuilder.Append($"&dn={Uri.EscapeDataString(entry.Title)}");
                    }

                    if (!string.IsNullOrWhiteSpace(entry.PrimaryTracker))
                    {
                        magnetBuilder.Append($"&tr={Uri.EscapeDataString(entry.PrimaryTracker)}");
                    }

                    magnetUri = magnetBuilder.ToString();
                }
            }
        }

        // 3. Parse torrent bytes if available
        ParsedTorrent parsed = null;
        if (torrentBytes != null && torrentBytes.Length > 0 && this.torrentFileParser != null)
        {
            try
            {
                parsed = this.torrentFileParser.Parse(torrentBytes);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to parse .torrent bytes for {0}", entry.InfoHash);
            }
        }

        // 4. Check if files already exist on disk or entry was completed
        var filesExistOnDisk = false;
        if (parsed?.Files != null && parsed.Files.Count > 0 && !string.IsNullOrWhiteSpace(effectiveSavePath))
        {
            try
            {
                filesExistOnDisk = parsed.Files.All(f =>
                {
                    var fullPath = Path.Combine(effectiveSavePath, f.Path);
                    return File.Exists(fullPath) && (f.Size <= 0 || new FileInfo(fullPath).Length == f.Size);
                });
            }
            catch
            {
                filesExistOnDisk = false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(effectiveSavePath) && !string.IsNullOrWhiteSpace(entry.Title))
        {
            try
            {
                var directPath = Path.Combine(effectiveSavePath, entry.Title);
                filesExistOnDisk = File.Exists(directPath) || Directory.Exists(directPath);
            }
            catch
            {
                filesExistOnDisk = false;
            }
        }

        var isCompleted = string.Equals(entry.Status, "Completed", StringComparison.OrdinalIgnoreCase)
            || entry.DateCompleted.HasValue
            || (entry.TotalSize > 0 && entry.Downloaded >= entry.TotalSize)
            || filesExistOnDisk;

        var name = parsed?.Name ?? entry.Title ?? "Unknown Release";
        var infoHash = (parsed?.InfoHash ?? entry.InfoHash ?? string.Empty).ToLowerInvariant();
        var totalSize = parsed?.TotalSize > 0 ? parsed.TotalSize : entry.TotalSize;
        var status = isCompleted ? TorrentStatus.Seeding : TorrentStatus.Downloading;
        var progress = isCompleted ? 1.0 : (totalSize > 0 && entry.Downloaded > 0 ? Math.Min(1.0, (double)entry.Downloaded / totalSize) : 0.0);
        var downloaded = isCompleted && entry.Downloaded <= 0 ? totalSize : entry.Downloaded;
        DateTime? dateCompleted = isCompleted ? (entry.DateCompleted ?? DateTime.UtcNow) : null;

        var torrent = new Torrent
        {
            Name = name,
            InfoHash = infoHash,
            TotalSize = totalSize,
            PieceCount = parsed?.PieceCount ?? 0,
            PieceLength = parsed?.PieceLength ?? 0,
            Comment = parsed?.Comment,
            CreatedBy = parsed?.CreatedBy,
            CreationDate = parsed?.CreationDate,
            IsPrivate = parsed?.IsPrivate ?? false,
            Status = status,
            Progress = progress,
            DateAdded = DateTime.UtcNow,
            DateCompleted = dateCompleted,
            Uploaded = entry.Uploaded,
            Downloaded = downloaded,
            Ratio = entry.Ratio,
            Category = effectiveCategory,
            SavePath = effectiveSavePath,
            TrackerUrl = parsed?.AnnounceUrl ?? entry.PrimaryTracker,
            TagIds = new List<int>(),
        };

        var all = this.torrentRepository.All().ToList();
        torrent.Priority = all.Count > 0 ? all.Max(t => t.Priority) + 1 : 0;
        torrent.QueuePosition = all.Select(t => t.QueuePosition).DefaultIfEmpty(0).Max() + 1;

        var added = this.torrentRepository.Insert(torrent);

        // 5. Insert torrent file records if parsed
        if (parsed?.Files != null && this.fileRepository != null)
        {
            var pieceLength = Math.Max(1, parsed.PieceLength);
            long currentByteOffset = 0;
            var torrentFiles = new List<TorrentFile>();
            foreach (var file in parsed.Files)
            {
                var startPiece = (int)(currentByteOffset / pieceLength);
                var endByte = currentByteOffset + file.Size - 1;
                var endPiece = file.Size > 0 ? (int)(endByte / pieceLength) : startPiece;
                var pieceCount = file.Size > 0 ? (endPiece - startPiece + 1) : 0;

                var torrentFile = new TorrentFile
                {
                    TorrentId = added.Id,
                    Path = file.Path,
                    Size = file.Size,
                    PieceOffset = startPiece,
                    PieceCount = pieceCount,
                    Priority = 1,
                    Progress = isCompleted ? 1.0 : 0.0,
                };
                torrentFiles.Add(torrentFile);
                currentByteOffset += file.Size;
            }

            if (torrentFiles.Count > 0)
            {
                this.fileRepository.InsertMany(torrentFiles);
            }
        }

        // 6. Insert trackers
        if (this.trackerEntryRepository != null)
        {
            if (parsed?.AnnounceList != null && parsed.AnnounceList.Count > 0)
            {
                var trackerEntries = new List<TrackerEntry>();
                for (var tier = 0; tier < parsed.AnnounceList.Count; tier++)
                {
                    foreach (var url in parsed.AnnounceList[tier])
                    {
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            trackerEntries.Add(new TrackerEntry
                            {
                                TorrentId = added.Id,
                                Url = url,
                                Tier = tier,
                                Enabled = true,
                                Status = 1,
                                AnnounceInterval = 1800,
                                LastAnnounce = added.DateAdded,
                                NextAnnounce = added.DateAdded.AddSeconds(1800),
                                TotalAnnounces = 1,
                                SuccessfulAnnounces = 1,
                            });
                        }
                    }
                }

                if (trackerEntries.Count > 0)
                {
                    this.trackerEntryRepository.InsertMany(trackerEntries);
                }
            }
            else if (!string.IsNullOrWhiteSpace(torrent.TrackerUrl))
            {
                this.trackerEntryRepository.Insert(new TrackerEntry
                {
                    TorrentId = added.Id,
                    Url = torrent.TrackerUrl,
                    Tier = 0,
                    Enabled = true,
                    Status = 1,
                    AnnounceInterval = 1800,
                    LastAnnounce = added.DateAdded,
                    NextAnnounce = added.DateAdded.AddSeconds(1800),
                    TotalAnnounces = 1,
                    SuccessfulAnnounces = 1,
                });
            }
            else if (!string.IsNullOrWhiteSpace(entry.PrimaryTracker))
            {
                this.trackerEntryRepository.Insert(new TrackerEntry
                {
                    TorrentId = added.Id,
                    Url = entry.PrimaryTracker,
                    Tier = 0,
                    Enabled = true,
                    Status = 1,
                    AnnounceInterval = 1800,
                    LastAnnounce = added.DateAdded,
                    NextAnnounce = added.DateAdded.AddSeconds(1800),
                    TotalAnnounces = 1,
                    SuccessfulAnnounces = 1,
                });
            }
        }

        // 7. Cache .torrent file
        if (torrentBytes != null && torrentBytes.Length > 0 && !string.IsNullOrWhiteSpace(added.InfoHash))
        {
            try
            {
                var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                    ? this.appFolderInfo.AppDataFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var torrentsDir = Path.Combine(appData, "Torrents");
                Directory.CreateDirectory(torrentsDir);
                var filePath = Path.Combine(torrentsDir, $"{added.InfoHash.ToLowerInvariant()}.torrent");
                await File.WriteAllBytesAsync(filePath, torrentBytes);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to save re-added .torrent file for {0}", added.InfoHash);
            }
        }

        entry.TorrentId = added.Id;
        entry.Status = "Active";
        entry.DateRemoved = null;
        this.historyRepository.Update(entry);

        try
        {
            await this.downloadEngine.AddTorrentAsync(added, torrentBytes, magnetUri);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to start engine for re-added historical torrent {0}", added.Id);
        }

        this.eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = added });

        this.logger.Info("Re-added historical torrent '{0}' (InfoHash: {1}) with ID {2}", entry.Title, entry.InfoHash, added.Id);
        return added;
    }

    public void Update(DownloadHistory history)
    {
        if (history != null)
        {
            this.historyRepository.Update(history);
        }
    }

    public int ReconcileAllTorrents()
    {
        var allTorrents = this.torrentRepository.All().ToList();
        var backfilled = 0;

        foreach (var torrent in allTorrents)
        {
            if (string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                continue;
            }

            var existing = this.historyRepository.FindByInfoHash(torrent.InfoHash);
            if (existing == null)
            {
                var tracker = this.trackerEntryRepository.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;

                var entry = new DownloadHistory
                {
                    TorrentId = torrent.Id,
                    Title = torrent.Name ?? torrent.InfoHash,
                    InfoHash = torrent.InfoHash.ToLowerInvariant(),
                    TotalSize = torrent.TotalSize,
                    DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
                    Uploaded = torrent.Uploaded,
                    Downloaded = torrent.Downloaded,
                    Ratio = torrent.Ratio,
                    PrimaryTracker = tracker,
                    Status = "Active",
                    SeedingTime = 0,
                    Source = torrent.IsPrivate ? "Private Tracker" : "Public Tracker",
                };

                this.historyRepository.Insert(entry);
                backfilled++;
            }
            else if (existing.TorrentId == null || existing.TorrentId == 0)
            {
                existing.TorrentId = torrent.Id;
                existing.Status = "Active";
                this.historyRepository.Update(existing);
            }
        }

        if (backfilled > 0)
        {
            this.logger.Info("Reconciled and backfilled {0} missing torrents into Download History", backfilled);
        }

        return backfilled;
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.RecordTorrentAdded(message.Torrent);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent != null)
        {
            this.RecordTorrentRemoved(message.Torrent, "Deleted from active library");
            return;
        }
    }
}
