// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Torrents;

public class TorrentService : ITorrentService, IHandle<TorrentDownloadCompletedEvent>, IHandle<CategoryUpdatedEvent>, IHandle<CategoryDeletedEvent>
{
    private static readonly SemaphoreSlim QueueLock = new(1, 1);
    private readonly ConcurrentDictionary<int, long> lastSeenSessionUploaded = new();
    private readonly Dictionary<int, RefCountedSemaphore> deletionLocks = new();
    private readonly object deletionLocksSync = new();
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentFileRepository fileRepository;
    private readonly ICategoryService categoryService;
    private readonly IMediaEnrichmentService mediaEnrichmentService;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IQueueManagerService queueManagerService;
    private readonly IStoragePathService storagePathService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly ITorrentLogService torrentLogService;
    private readonly ISpeedSchedulerService speedSchedulerService;
    private readonly Logger logger;

    public TorrentService(
        ITorrentRepository torrentRepository,
        ITorrentFileRepository fileRepository,
        ICategoryService categoryService,
        IMediaEnrichmentService mediaEnrichmentService,
        IConfigService configService,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator,
        ITrackerEntryRepository trackerEntryRepository = null,
        IQueueManagerService queueManagerService = null,
        IStoragePathService storagePathService = null,
        IAppFolderInfo appFolderInfo = null,
        ITorrentLogService torrentLogService = null,
        ISpeedSchedulerService speedSchedulerService = null)
    {
        this.torrentRepository = torrentRepository;
        this.fileRepository = fileRepository;
        this.categoryService = categoryService;
        this.mediaEnrichmentService = mediaEnrichmentService;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
        this.trackerEntryRepository = trackerEntryRepository;
        this.queueManagerService = queueManagerService;
        this.storagePathService = storagePathService;
        this.appFolderInfo = appFolderInfo;
        this.torrentLogService = torrentLogService;
        this.speedSchedulerService = speedSchedulerService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<Torrent> GetAll()
    {
        var torrents = this.torrentRepository.All().OrderBy(t => t.QueuePosition > 0 ? t.QueuePosition : t.Id).ToList();
        var toUpdate = new List<Torrent>();
        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];
            this.SyncWithEngine(torrent);
            if (torrent.QueuePosition <= 0)
            {
                torrent.QueuePosition = i + 1;
                toUpdate.Add(torrent);
            }
        }

        if (toUpdate.Count > 0)
        {
            this.torrentRepository.UpdateMany(toUpdate);
        }

        return torrents;
    }

    public Torrent Get(int id)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null)
        {
            this.SyncWithEngine(torrent);
            if (torrent.QueuePosition <= 0)
            {
                torrent.QueuePosition = this.torrentRepository.GetNextQueuePosition();
                this.torrentRepository.Update(torrent);
            }
        }

        return torrent;
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var torrent = this.torrentRepository.GetByInfoHash(infoHash.ToLowerInvariant());
        if (torrent != null)
        {
            this.SyncWithEngine(torrent);
        }

        return torrent;
    }

    public async Task<Torrent> AddFromParsedTorrentAsync(
        ParsedTorrent parsed,
        string category = null,
        string savePath = null,
        bool startPaused = false,
        byte[] rawBytes = null)
    {
        if (parsed == null)
        {
            throw new ArgumentNullException(nameof(parsed));
        }

        var existing = this.GetByInfoHash(parsed.InfoHash);
        if (existing != null)
        {
            this.logger.Warn("Torrent with infohash {0} already exists", parsed.InfoHash);
            return existing;
        }

        var effectiveCategory = !string.IsNullOrWhiteSpace(category) ? category : this.configService.DefaultCategory;
        var defaultDownloadDir = this.storagePathService?.GetCompletedDirectory(effectiveCategory);
        if (string.IsNullOrWhiteSpace(defaultDownloadDir))
        {
            var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                ? this.appFolderInfo.AppDataFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
            var fallbackPath = this.configService.DownloadDir ?? Path.Combine(appData, "downloads");
            defaultDownloadDir = this.categoryService.GetSavePathForCategory(effectiveCategory, fallbackPath);
        }

        var effectiveSavePath = !string.IsNullOrWhiteSpace(savePath)
            ? savePath
            : defaultDownloadDir;

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash.ToLowerInvariant(),
            TotalSize = parsed.TotalSize,
            PieceCount = parsed.PieceCount,
            PieceLength = parsed.PieceLength,
            Comment = parsed.Comment,
            CreatedBy = parsed.CreatedBy,
            CreationDate = parsed.CreationDate,
            IsPrivate = parsed.IsPrivate,
            TrackerUrl = parsed.AnnounceList?.SelectMany(tier => tier).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? parsed.AnnounceUrl,
            Status = startPaused ? TorrentStatus.Paused : TorrentStatus.Downloading,
            Category = effectiveCategory,
            SavePath = effectiveSavePath,
            QueuePosition = this.torrentRepository.GetNextQueuePosition(),
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>(),
        };

        var cat = !string.IsNullOrWhiteSpace(effectiveCategory)
            ? this.categoryService.GetByName(effectiveCategory)
            : null;

        if (cat != null)
        {
            if (torrent.TargetRatio <= 0)
            {
                torrent.TargetRatio = cat.TargetRatio;
            }

            if (torrent.TargetSeedTimeMinutes <= 0)
            {
                torrent.TargetSeedTimeMinutes = cat.TargetSeedTimeMinutes;
            }
        }

        var inserted = this.torrentRepository.Insert(torrent) ?? torrent;

        // Insert torrent files
        if (parsed.Files != null)
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
                    TorrentId = inserted.Id,
                    Path = file.Path,
                    Size = file.Size,
                    PieceOffset = startPiece,
                    PieceCount = pieceCount,
                    ByteOffset = currentByteOffset,
                    Priority = 3,
                    Progress = 0.0,
                };
                torrentFiles.Add(torrentFile);
                currentByteOffset += file.Size;
            }

            if (torrentFiles.Count > 0)
            {
                this.fileRepository.InsertMany(torrentFiles);
            }
        }

        var defaultAnnounceInterval = this.configService?.AnnounceIntervalSeconds > 0
            ? this.configService.AnnounceIntervalSeconds
            : (this.configService?.TrackerAnnounceInterval > 0 ? this.configService.TrackerAnnounceInterval : 1800);

        // Insert trackers from torrent
        if (this.trackerEntryRepository != null)
        {
            if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
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
                                TorrentId = inserted.Id,
                                Url = url,
                                Tier = tier,
                                Enabled = true,
                                Status = 0,
                                AnnounceInterval = defaultAnnounceInterval,
                                LastAnnounce = null,
                                NextAnnounce = inserted.DateAdded.AddSeconds(defaultAnnounceInterval),
                                TotalAnnounces = 0,
                                SuccessfulAnnounces = 0,
                            });
                        }
                    }
                }

                if (trackerEntries.Count > 0)
                {
                    this.trackerEntryRepository.InsertMany(trackerEntries);
                }
            }
            else if (!string.IsNullOrWhiteSpace(parsed.AnnounceUrl))
            {
                this.trackerEntryRepository.Insert(new TrackerEntry
                {
                    TorrentId = inserted.Id,
                    Url = parsed.AnnounceUrl,
                    Tier = 0,
                    Enabled = true,
                    Status = 0,
                    AnnounceInterval = defaultAnnounceInterval,
                    LastAnnounce = null,
                    NextAnnounce = inserted.DateAdded.AddSeconds(defaultAnnounceInterval),
                    TotalAnnounces = 0,
                    SuccessfulAnnounces = 0,
                });
            }
        }

        this.logger.Info("Added torrent: {0} ({1})", inserted.Name, inserted.InfoHash);

        if (rawBytes != null && rawBytes.Length > 0 && !string.IsNullOrWhiteSpace(inserted.InfoHash))
        {
            try
            {
                var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                    ? this.appFolderInfo.AppDataFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var torrentsDir = Path.Combine(appData, "Torrents");
                Directory.CreateDirectory(torrentsDir);
                var filePath = Path.Combine(torrentsDir, $"{inserted.InfoHash.ToLowerInvariant()}.torrent");
                await File.WriteAllBytesAsync(filePath, rawBytes);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to save ingested .torrent file for {0}", inserted.InfoHash);
            }
        }

        // Start torrent in BitTorrent download engine
        try
        {
            await this.downloadEngine.AddTorrentAsync(inserted, rawBytes, null);
            if (startPaused)
            {
                await this.downloadEngine.PauseTorrentAsync(inserted.Id);
            }

            var effectiveDl = this.GetEffectiveDownloadLimit(inserted);
            var effectiveUl = this.GetEffectiveUploadLimit(inserted);
            if (this.downloadEngine != null && (effectiveDl > 0 || effectiveUl > 0))
            {
                await this.downloadEngine.SetTorrentRateLimitsAsync(inserted.Id, effectiveDl, effectiveUl);
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to start download engine task for torrent {0}", inserted.Name);
        }

        this.eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = inserted });

        // Trigger asynchronous media enrichment
        if (this.configService.AutoEnrichEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.mediaEnrichmentService.EnrichTorrentAsync(inserted);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Background media enrichment failed for torrent {0}", inserted.Id);
                }
            });
        }

        return inserted;
    }

    public async Task<Torrent> AddFromMagnetAsync(
        string magnetUri,
        string category = null,
        string savePath = null,
        bool startPaused = false)
    {
        var parsedMagnet = MagnetLinkParser.Parse(magnetUri);
        var existing = this.GetByInfoHash(parsedMagnet?.InfoHash);
        if (existing != null)
        {
            this.logger.Warn("Torrent with infohash {0} already exists", parsedMagnet.InfoHash);
            return existing;
        }

        var effectiveCategory = !string.IsNullOrWhiteSpace(category) ? category : this.configService.DefaultCategory;
        var defaultDownloadDir = this.storagePathService?.GetCompletedDirectory(effectiveCategory);
        if (string.IsNullOrWhiteSpace(defaultDownloadDir))
        {
            var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                ? this.appFolderInfo.AppDataFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
            var fallbackPath = this.configService.DownloadDir ?? Path.Combine(appData, "downloads");
            defaultDownloadDir = this.categoryService.GetSavePathForCategory(effectiveCategory, fallbackPath);
        }

        var effectiveSavePath = !string.IsNullOrWhiteSpace(savePath)
            ? savePath
            : defaultDownloadDir;

        var torrent = new Torrent
        {
            Name = !string.IsNullOrWhiteSpace(parsedMagnet.DisplayName) ? parsedMagnet.DisplayName : parsedMagnet.InfoHash,
            InfoHash = parsedMagnet.InfoHash.ToLowerInvariant(),
            TotalSize = 0,
            PieceCount = 0,
            PieceLength = 0,
            TrackerUrl = parsedMagnet.Trackers?.FirstOrDefault(),
            Status = startPaused ? TorrentStatus.Paused : TorrentStatus.Downloading,
            Category = effectiveCategory,
            SavePath = effectiveSavePath,
            QueuePosition = this.torrentRepository.GetNextQueuePosition(),
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>(),
        };

        var cat = !string.IsNullOrWhiteSpace(effectiveCategory)
            ? this.categoryService.GetByName(effectiveCategory)
            : null;

        if (cat != null)
        {
            if (torrent.TargetRatio <= 0)
            {
                torrent.TargetRatio = cat.TargetRatio;
            }

            if (torrent.TargetSeedTimeMinutes <= 0)
            {
                torrent.TargetSeedTimeMinutes = cat.TargetSeedTimeMinutes;
            }
        }

        var inserted = this.torrentRepository.Insert(torrent) ?? torrent;

        var defaultAnnounceInterval = this.configService?.AnnounceIntervalSeconds > 0
            ? this.configService.AnnounceIntervalSeconds
            : (this.configService?.TrackerAnnounceInterval > 0 ? this.configService.TrackerAnnounceInterval : 1800);

        // Insert trackers from magnet
        if (this.trackerEntryRepository != null && parsedMagnet.Trackers != null)
        {
            foreach (var trackerUrl in parsedMagnet.Trackers)
            {
                if (!string.IsNullOrWhiteSpace(trackerUrl))
                {
                    this.trackerEntryRepository.Insert(new TrackerEntry
                    {
                        TorrentId = inserted.Id,
                        Url = trackerUrl,
                        Tier = 0,
                        Enabled = true,
                        Status = 0,
                        AnnounceInterval = defaultAnnounceInterval,
                        LastAnnounce = null,
                        NextAnnounce = inserted.DateAdded.AddSeconds(defaultAnnounceInterval),
                        TotalAnnounces = 0,
                        SuccessfulAnnounces = 0,
                    });
                }
            }
        }

        this.logger.Info("Added magnet torrent: {0} ({1})", inserted.Name, inserted.InfoHash);

        // Start torrent in BitTorrent download engine
        try
        {
            await this.downloadEngine.AddTorrentAsync(inserted, null, magnetUri);
            if (startPaused)
            {
                await this.downloadEngine.PauseTorrentAsync(inserted.Id);
            }

            var effectiveDl = this.GetEffectiveDownloadLimit(inserted);
            var effectiveUl = this.GetEffectiveUploadLimit(inserted);
            if (this.downloadEngine != null && (effectiveDl > 0 || effectiveUl > 0))
            {
                await this.downloadEngine.SetTorrentRateLimitsAsync(inserted.Id, effectiveDl, effectiveUl);
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to start download engine task for magnet {0}", inserted.Name);
        }

        this.eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = inserted });

        // Trigger asynchronous media enrichment
        if (this.configService.AutoEnrichEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.mediaEnrichmentService.EnrichTorrentAsync(inserted);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Background media enrichment failed for magnet {0}", inserted.Id);
                }
            });
        }

        return inserted;
    }

    public async Task<Torrent> UpdateAsync(Torrent torrent)
    {
        if (torrent == null)
        {
            throw new ArgumentNullException(nameof(torrent));
        }

        var existing = this.torrentRepository.Get(torrent.Id);
        if (existing != null && !string.Equals(existing.Category, torrent.Category, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(torrent.Category))
        {
            var cat = this.categoryService.GetByName(torrent.Category);
            if (cat != null)
            {
                if (torrent.TargetRatio <= 0)
                {
                    torrent.TargetRatio = cat.TargetRatio;
                }

                if (torrent.TargetSeedTimeMinutes <= 0)
                {
                    torrent.TargetSeedTimeMinutes = cat.TargetSeedTimeMinutes;
                }
            }
        }

        var effectiveDl = this.GetEffectiveDownloadLimit(torrent);
        var effectiveUl = this.GetEffectiveUploadLimit(torrent);

        if (this.downloadEngine != null)
        {
            try
            {
                await this.downloadEngine.SetTorrentRateLimitsAsync(torrent.Id, effectiveDl, effectiveUl);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to apply rate limits to download engine for torrent {0}", torrent.Id);
            }
        }

        var updated = this.torrentRepository.Update(torrent);
        this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = updated });
        return await Task.FromResult(updated);
    }

    public async Task DeleteAsync(int id, bool deleteFiles = false)
    {
        RefCountedSemaphore lockItem;
        lock (this.deletionLocksSync)
        {
            if (this.deletionLocks.TryGetValue(id, out var existing))
            {
                existing.RefCount++;
                lockItem = existing;
            }
            else
            {
                lockItem = new RefCountedSemaphore();
                this.deletionLocks[id] = lockItem;
            }
        }

        await lockItem.Semaphore.WaitAsync();
        try
        {
            var torrent = this.torrentRepository.Get(id);
            if (torrent == null)
            {
                return;
            }

            this.logger.Info("Deleting torrent {0} (DeleteFiles={1})", torrent.Name, deleteFiles);

            var torrentFiles = this.fileRepository?.GetByTorrentId(id)?.ToList() ?? new List<TorrentFile>();

            try
            {
                await this.downloadEngine.RemoveTorrentAsync(id, deleteFiles);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error removing torrent {0} from download engine", id);
            }

            this.fileRepository.DeleteByTorrentId(id);
            this.trackerEntryRepository?.DeleteByTorrentId(id);
            this.mediaEnrichmentService.DeleteMetadata(id);
            this.mediaEnrichmentService.CleanupTorrentCache(id);
            this.torrentRepository.Delete(id);

            try
            {
                var hash = torrent.InfoHash?.ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(hash))
                {
                    var pathsToTry = new List<string>();

                    if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
                    {
                        pathsToTry.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Torrents", $"{hash}.torrent"));
                        pathsToTry.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Leecharr", "Torrents", $"{hash}.torrent"));
                    }

                    var legacyAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (!string.IsNullOrWhiteSpace(legacyAppData))
                    {
                        pathsToTry.Add(Path.Combine(legacyAppData, "Torrents", $"{hash}.torrent"));
                        pathsToTry.Add(Path.Combine(legacyAppData, "Leecharr", "Torrents", $"{hash}.torrent"));
                    }

                    foreach (var path in pathsToTry.Distinct())
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                }
            }
            catch
            {
            }

            if (deleteFiles)
            {
                await this.DeleteTorrentDataOnDiskAsync(torrent, torrentFiles);
            }

            this.eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = deleteFiles });
        }
        finally
        {
            lockItem.Semaphore.Release();
            lock (this.deletionLocksSync)
            {
                lockItem.RefCount--;
                if (lockItem.RefCount <= 0)
                {
                    this.deletionLocks.Remove(id);
                    lockItem.Semaphore.Dispose();
                }
            }
        }
    }

    private async Task DeleteTorrentDataOnDiskAsync(Torrent torrent, List<TorrentFile> torrentFiles)
    {
        var isIncomplete = torrent.Progress < 1.0 ||
                           torrent.Status == TorrentStatus.Downloading ||
                           torrent.Status == TorrentStatus.Queued;

        if (isIncomplete)
        {
            await this.PurgeIncompleteChunksAsync(torrent);
        }

        if (string.IsNullOrWhiteSpace(torrent.Name))
        {
            return;
        }

        try
        {
            var sanitizedName = TorrentPathValidator.SanitizeRelativePath(torrent.Name);

            // 1. If torrent.SavePath is directly a file, delete it
            if (!string.IsNullOrWhiteSpace(torrent.SavePath) && File.Exists(torrent.SavePath))
            {
                if (!this.IsProtectedRoot(torrent.SavePath))
                {
                    await DeletePathWithRetryAsync(torrent.SavePath, isDirectory: false);
                }
            }

            // 2. If torrent.SavePath is a directory
            if (!string.IsNullOrWhiteSpace(torrent.SavePath) && Directory.Exists(torrent.SavePath))
            {
                var savePathDirName = Path.GetFileName(torrent.SavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var isDedicatedFolder = string.Equals(savePathDirName, torrent.Name, StringComparison.OrdinalIgnoreCase) ||
                                       (!string.IsNullOrWhiteSpace(sanitizedName) && string.Equals(savePathDirName, sanitizedName, StringComparison.OrdinalIgnoreCase));

                if (isDedicatedFolder && !this.IsProtectedRoot(torrent.SavePath))
                {
                    await DeletePathWithRetryAsync(torrent.SavePath, isDirectory: true);
                }
                else
                {
                    // SavePath is a parent/root directory (e.g. /downloads/tv), check child folder or file matching torrent name
                    var childCandidates = new List<string> { Path.Combine(torrent.SavePath, torrent.Name) };
                    if (!string.IsNullOrWhiteSpace(sanitizedName))
                    {
                        childCandidates.Add(Path.Combine(torrent.SavePath, sanitizedName));
                    }

                    foreach (var childPath in childCandidates.Distinct())
                    {
                        if (TorrentPathValidator.IsStrictSubPath(torrent.SavePath, childPath) && !this.IsProtectedRoot(childPath))
                        {
                            if (Directory.Exists(childPath))
                            {
                                await DeletePathWithRetryAsync(childPath, isDirectory: true);
                            }
                            else if (File.Exists(childPath))
                            {
                                await DeletePathWithRetryAsync(childPath, isDirectory: false);
                            }
                        }
                    }
                }
            }

            // 3. Check category completed directory and global completed directory
            var candidateRoots = new List<string>();
            if (this.storagePathService != null)
            {
                var catCompleted = this.storagePathService.GetCompletedDirectory(torrent.Category);
                if (!string.IsNullOrWhiteSpace(catCompleted))
                {
                    candidateRoots.Add(catCompleted);
                }

                var globalCompleted = this.storagePathService.GetCompletedDirectory(null);
                if (!string.IsNullOrWhiteSpace(globalCompleted))
                {
                    candidateRoots.Add(globalCompleted);
                }
            }

            foreach (var root in candidateRoots.Distinct())
            {
                if (Directory.Exists(root))
                {
                    var targets = new List<string> { Path.Combine(root, torrent.Name) };
                    if (!string.IsNullOrWhiteSpace(sanitizedName))
                    {
                        targets.Add(Path.Combine(root, sanitizedName));
                    }

                    foreach (var target in targets.Distinct())
                    {
                        if (TorrentPathValidator.IsStrictSubPath(root, target) && !this.IsProtectedRoot(target))
                        {
                            if (Directory.Exists(target))
                            {
                                await DeletePathWithRetryAsync(target, isDirectory: true);
                            }
                            else if (File.Exists(target))
                            {
                                await DeletePathWithRetryAsync(target, isDirectory: false);
                            }
                        }
                    }
                }
            }

            // 4. Delete individual files known from torrent file list if any remain
            if (torrentFiles != null && torrentFiles.Count > 0)
            {
                var searchDirs = new List<string>();
                if (!string.IsNullOrWhiteSpace(torrent.SavePath) && Directory.Exists(torrent.SavePath))
                {
                    searchDirs.Add(torrent.SavePath);
                }

                if (this.storagePathService != null)
                {
                    var catDir = this.storagePathService.GetCompletedDirectory(torrent.Category);
                    if (!string.IsNullOrWhiteSpace(catDir) && Directory.Exists(catDir))
                    {
                        searchDirs.Add(catDir);
                    }

                    var incDir = this.storagePathService.GetIncompleteDirectory();
                    if (!string.IsNullOrWhiteSpace(incDir) && Directory.Exists(incDir))
                    {
                        searchDirs.Add(incDir);
                    }
                }

                foreach (var file in torrentFiles)
                {
                    if (string.IsNullOrWhiteSpace(file?.Path))
                    {
                        continue;
                    }

                    foreach (var searchDir in searchDirs.Distinct())
                    {
                        var candidateFilePaths = new List<string>
                        {
                            Path.Combine(searchDir, file.Path),
                            Path.Combine(searchDir, torrent.Name, file.Path),
                        };

                        if (!string.IsNullOrWhiteSpace(sanitizedName))
                        {
                            candidateFilePaths.Add(Path.Combine(searchDir, sanitizedName, file.Path));
                        }

                        foreach (var cand in candidateFilePaths.Distinct())
                        {
                            if (File.Exists(cand) && !this.IsProtectedRoot(cand))
                            {
                                await DeletePathWithRetryAsync(cand, isDirectory: false);
                            }

                            var candidateExtensions = new[] { ".!mt", ".!leech", ".incomplete", this.configService?.IncompleteExtension };
                            foreach (var ext in candidateExtensions)
                            {
                                if (string.IsNullOrWhiteSpace(ext))
                                {
                                    continue;
                                }

                                var extFile = cand + ext;
                                if (File.Exists(extFile) && !this.IsProtectedRoot(extFile))
                                {
                                    await DeletePathWithRetryAsync(extFile, isDirectory: false);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to delete files for torrent {0}", torrent.Name);
        }
    }

    private bool IsProtectedRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            var canonical = TorrentPathValidator.ResolveCanonicalPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(canonical)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (string.Equals(canonical, root, comparison) || string.IsNullOrEmpty(canonical))
            {
                return true;
            }

            var protectedRoots = new List<string>();

            if (this.storagePathService != null)
            {
                var incomplete = this.storagePathService.GetIncompleteDirectory();
                if (!string.IsNullOrWhiteSpace(incomplete))
                {
                    protectedRoots.Add(incomplete);
                }

                var completed = this.storagePathService.GetCompletedDirectory(null);
                if (!string.IsNullOrWhiteSpace(completed))
                {
                    protectedRoots.Add(completed);
                }
            }

            if (this.configService != null)
            {
                if (!string.IsNullOrWhiteSpace(this.configService.DownloadDir))
                {
                    protectedRoots.Add(this.configService.DownloadDir);
                }

                if (!string.IsNullOrWhiteSpace(this.configService.IncompleteDownloadDir))
                {
                    protectedRoots.Add(this.configService.IncompleteDownloadDir);
                }
            }

            if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
            {
                protectedRoots.Add(this.appFolderInfo.AppDataFolder);
                protectedRoots.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "downloads"));
                protectedRoots.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "downloads", "incomplete"));
                protectedRoots.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "downloads", "complete"));
            }

            if (this.categoryService != null)
            {
                var categories = this.categoryService.GetAll();
                if (categories != null)
                {
                    foreach (var cat in categories)
                    {
                        if (!string.IsNullOrWhiteSpace(cat.SavePath))
                        {
                            protectedRoots.Add(cat.SavePath);
                        }

                        if (this.storagePathService != null && !string.IsNullOrWhiteSpace(cat.Name))
                        {
                            var catDir = this.storagePathService.GetCompletedDirectory(cat.Name);
                            if (!string.IsNullOrWhiteSpace(catDir))
                            {
                                protectedRoots.Add(catDir);
                            }
                        }
                    }
                }
            }

            foreach (var protectedRoot in protectedRoots)
            {
                if (string.IsNullOrWhiteSpace(protectedRoot))
                {
                    continue;
                }

                var canonicalProtected = TorrentPathValidator.ResolveCanonicalPath(protectedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(canonical, canonicalProtected, comparison))
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private async Task PurgeIncompleteChunksAsync(Torrent torrent)
    {
        try
        {
            var incompleteDir = this.storagePathService?.GetIncompleteDirectory();
            if (string.IsNullOrWhiteSpace(incompleteDir))
            {
                incompleteDir = this.configService?.IncompleteDownloadDir;
            }

            if (string.IsNullOrWhiteSpace(incompleteDir))
            {
                var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                    ? this.appFolderInfo.AppDataFolder
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
                incompleteDir = Path.Combine(appData, "downloads", "incomplete");
            }

            if (string.IsNullOrWhiteSpace(incompleteDir) || !Directory.Exists(incompleteDir))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(torrent.Name))
            {
                var incompleteFolder = Path.Combine(incompleteDir, torrent.Name);
                if (TorrentPathValidator.IsStrictSubPath(incompleteDir, incompleteFolder))
                {
                    if (Directory.Exists(incompleteFolder))
                    {
                        await DeletePathWithRetryAsync(incompleteFolder, isDirectory: true);
                    }
                    else if (File.Exists(incompleteFolder))
                    {
                        await DeletePathWithRetryAsync(incompleteFolder, isDirectory: false);
                    }
                }

                var candidateExtensions = new[] { ".!mt", ".!leech", ".incomplete", this.configService?.IncompleteExtension };
                foreach (var ext in candidateExtensions)
                {
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        continue;
                    }

                    var extFile = Path.Combine(incompleteDir, torrent.Name + ext);
                    if (TorrentPathValidator.IsStrictSubPath(incompleteDir, extFile) && File.Exists(extFile))
                    {
                        await DeletePathWithRetryAsync(extFile, isDirectory: false);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                var hashDir = Path.Combine(incompleteDir, torrent.InfoHash);
                if (TorrentPathValidator.IsStrictSubPath(incompleteDir, hashDir) && Directory.Exists(hashDir))
                {
                    await DeletePathWithRetryAsync(hashDir, isDirectory: true);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to purge incomplete chunks for torrent {0}", torrent.Name);
        }
    }

    private static async Task DeletePathWithRetryAsync(string path, bool isDirectory, int maxRetries = 3)
    {
        var delayMs = 150;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (isDirectory)
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
                else
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(delayMs);
                delayMs *= 2;
            }
        }
    }

    public async Task PauseAsync(int id)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null && torrent.Status != TorrentStatus.Paused)
        {
            var old = torrent.Status;
            torrent.Status = TorrentStatus.Paused;
            torrent.DownloadSpeed = 0;
            torrent.UploadSpeed = 0;
            torrent.Eta = 0;
            torrent.Seeders = 0;
            torrent.Leechers = 0;
            this.torrentRepository.Update(torrent);

            try
            {
                await this.downloadEngine.PauseTorrentAsync(id);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error pausing torrent in download engine {0}", id);
            }

            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = TorrentStatus.Paused });
        }
    }

    public async Task ResumeAsync(int id)
    {
        if (this.downloadEngine?.IsHaltedByKillSwitch == true)
        {
            this.logger.Warn("Cannot resume torrent id {0}: VPN Kill Switch is active (fail-closed).", id);
            return;
        }

        var torrent = this.torrentRepository.Get(id);
        if (torrent != null)
        {
            this.SyncWithEngine(torrent);
            var newStatus = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
            var old = torrent.Status;
            torrent.Status = newStatus;
            this.torrentRepository.Update(torrent);

            try
            {
                await this.downloadEngine.ResumeTorrentAsync(id);
                this.torrentLogService?.Log(id, "Info", "Engine", $"Torrent resumed ({newStatus})");
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error resuming torrent in download engine {0}", id);
                this.torrentLogService?.Log(id, "Warn", "Engine", $"Resume failed: {ex.Message}");
            }

            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = newStatus });
        }
    }

    public async Task ForceRecheckAsync(int id)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null)
        {
            var old = torrent.Status;
            torrent.Status = TorrentStatus.Checking;
            this.torrentRepository.Update(torrent);
            this.torrentLogService?.Log(id, "Info", "Engine", "Manual force recheck initiated. Verifying piece hashes on disk...");

            try
            {
                await this.downloadEngine.ForceRecheckAsync(id);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error rechecking torrent in download engine {0}", id);
                this.torrentLogService?.Log(id, "Warn", "Engine", $"Recheck failed: {ex.Message}");
            }

            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = TorrentStatus.Checking });
        }
    }

    public async Task ForceAnnounceAsync(int id)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null)
        {
            this.torrentLogService?.Log(id, "Info", "Tracker", "Manual tracker update requested (announcing to all trackers...)");
        }

        try
        {
            await this.downloadEngine.ForceAnnounceAsync(id);
            if (torrent != null)
            {
                this.torrentLogService?.Log(id, "Info", "Tracker", "Tracker announce signal dispatched successfully");
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error announcing torrent in download engine {0}", id);
            if (torrent != null)
            {
                this.torrentLogService?.Log(id, "Warn", "Tracker", $"Tracker announce failed: {ex.Message}");
            }
        }
    }

    public async Task MoveQueueAsync(int id, string position)
    {
        await QueueLock.WaitAsync();
        try
        {
            var torrent = this.torrentRepository.Get(id);
            if (torrent == null)
            {
                return;
            }

            var allTorrents = this.torrentRepository.All().OrderBy(t => t.QueuePosition).ToList();
            var index = allTorrents.FindIndex(t => t.Id == id);
            if (index < 0)
            {
                return;
            }

            allTorrents.RemoveAt(index);

            switch (position?.ToLowerInvariant())
            {
                case "top":
                    allTorrents.Insert(0, torrent);
                    break;
                case "up":
                    allTorrents.Insert(Math.Max(0, index - 1), torrent);
                    break;
                case "down":
                    allTorrents.Insert(Math.Min(allTorrents.Count, index + 1), torrent);
                    break;
                case "bottom":
                    allTorrents.Add(torrent);
                    break;
                default:
                    allTorrents.Insert(index, torrent);
                    break;
            }

            for (var i = 0; i < allTorrents.Count; i++)
            {
                allTorrents[i].QueuePosition = i + 1;
            }

            this.torrentRepository.UpdateMany(allTorrents);

            if (this.eventAggregator != null)
            {
                foreach (var t in allTorrents)
                {
                    this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = t });
                }
            }

            if (this.queueManagerService != null)
            {
                await this.queueManagerService.ProcessQueueAsync();
            }
        }
        finally
        {
            QueueLock.Release();
        }
    }

    private void SyncWithEngine(Torrent torrent)
    {
        var task = this.downloadEngine.GetTask(torrent.Id);
        if (task != null)
        {
            var oldStatus = torrent.Status;
            torrent.Status = task.Status;
            torrent.ErrorMessage = task.ErrorMessage;
            torrent.Progress = task.Progress;

            if (torrent.Status != TorrentStatus.Checking)
            {
                if (torrent.Progress >= 1.0 && torrent.TotalSize > 0)
                {
                    torrent.Downloaded = torrent.TotalSize;
                }
                else if (torrent.TotalSize > 0)
                {
                    torrent.Downloaded = Math.Max(torrent.Downloaded, (long)(torrent.TotalSize * torrent.Progress));
                }
                else if (task.DownloadedBytes > 0)
                {
                    torrent.Downloaded = Math.Max(torrent.Downloaded, task.DownloadedBytes);
                }
            }

            var currentSessionUploaded = task.UploadedBytes;
            if (currentSessionUploaded > 0)
            {
                this.lastSeenSessionUploaded.AddOrUpdate(
                    torrent.Id,
                    currentSessionUploaded,
                    (id, last) =>
                    {
                        if (currentSessionUploaded > last)
                        {
                            var delta = currentSessionUploaded - last;
                            torrent.Uploaded += delta;
                            return currentSessionUploaded;
                        }

                        return last;
                    });

                if (torrent.Uploaded < currentSessionUploaded)
                {
                    torrent.Uploaded = currentSessionUploaded;
                }
            }

            var isInactive = torrent.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued;
            torrent.DownloadSpeed = isInactive ? 0 : task.DownloadSpeed;
            torrent.UploadSpeed = isInactive ? 0 : task.UploadSpeed;
            torrent.Seeders = isInactive ? 0 : task.ConnectedSeeders;
            torrent.Leechers = isInactive ? 0 : task.ConnectedLeechers;

            var divisor = torrent.Downloaded > 0 ? torrent.Downloaded : (torrent.TotalSize > 0 ? torrent.TotalSize : 0);
            torrent.Ratio = divisor > 0 ? (double)torrent.Uploaded / divisor : 0.0;

            if (isInactive || torrent.DownloadSpeed <= 0 || torrent.Progress >= 1.0)
            {
                torrent.Eta = 0;
            }
            else
            {
                var remainingBytes = torrent.TotalSize > 0
                    ? Math.Max(0, torrent.TotalSize - (long)(torrent.TotalSize * torrent.Progress))
                    : Math.Max(0, torrent.TotalSize - torrent.Downloaded);
                torrent.Eta = remainingBytes / torrent.DownloadSpeed;
            }

            if (oldStatus != torrent.Status)
            {
                this.torrentRepository.Update(torrent);
                this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                {
                    Torrent = torrent,
                    OldStatus = oldStatus,
                    NewStatus = torrent.Status,
                });
            }

            // Record completion timestamp when torrent reaches Seeding
            if (torrent.Status == TorrentStatus.Seeding && !torrent.DateCompleted.HasValue)
            {
                torrent.DateCompleted = DateTime.UtcNow;
                this.torrentRepository.Update(torrent);
            }
        }
        else if (torrent.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued)
        {
            torrent.DownloadSpeed = 0;
            torrent.UploadSpeed = 0;
            torrent.Eta = 0;
            torrent.Seeders = 0;
            torrent.Leechers = 0;
        }

        if (string.IsNullOrWhiteSpace(torrent.TrackerUrl) && this.trackerEntryRepository != null)
        {
            var firstTracker = this.trackerEntryRepository.GetByTorrentId(torrent.Id).FirstOrDefault();
            if (firstTracker != null && !string.IsNullOrWhiteSpace(firstTracker.Url))
            {
                torrent.TrackerUrl = firstTracker.Url;
            }
        }
    }

    public IDownloadTask GetDownloadTask(int torrentId)
    {
        return this.downloadEngine.GetTask(torrentId);
    }

    public static bool IsStrictSubPath(string basePath, string targetPath)
    {
        return TorrentPathValidator.IsStrictSubPath(basePath, targetPath);
    }

    public async Task<bool> RenameFileAsync(int torrentId, string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath) ||
            !TorrentPathValidator.IsValidRelativePath(newPath) || !TorrentPathValidator.IsValidRelativePath(oldPath))
        {
            this.logger.Warn("Refusing to rename file in torrent {0}: invalid or unsafe path (old: '{1}', new: '{2}')", torrentId, oldPath, newPath);
            return false;
        }

        var torrent = this.torrentRepository?.Get(torrentId);
        if (torrent != null && !string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            var targetFullPath = Path.Combine(torrent.SavePath, newPath.Replace('\\', '/').TrimStart('/'));
            if (!TorrentPathValidator.IsStrictSubPath(torrent.SavePath, targetFullPath))
            {
                this.logger.Warn("Refusing to rename file in torrent {0}: target path '{1}' escapes save path '{2}'", torrentId, newPath, torrent.SavePath);
                return false;
            }
        }

        var result = await this.downloadEngine.RenameFileAsync(torrentId, oldPath, newPath);
        if (result && this.fileRepository != null)
        {
            var normalizedOld = oldPath.Replace('\\', '/').TrimStart('/');
            var normalizedNew = newPath.Replace('\\', '/').TrimStart('/');
            var dbFiles = this.fileRepository.GetByTorrentId(torrentId);
            var matching = dbFiles.FirstOrDefault(f => !string.IsNullOrEmpty(f.Path) && f.Path.Replace('\\', '/').TrimStart('/').Equals(normalizedOld, StringComparison.OrdinalIgnoreCase));
            if (matching != null)
            {
                matching.Path = normalizedNew;
                this.fileRepository.Update(matching);
            }
        }

        return result;
    }

    public async Task<bool> RenameFolderAsync(int torrentId, string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath) ||
            !TorrentPathValidator.IsValidRelativePath(newPath) || !TorrentPathValidator.IsValidRelativePath(oldPath))
        {
            this.logger.Warn("Refusing to rename folder in torrent {0}: invalid or unsafe path (old: '{1}', new: '{2}')", torrentId, oldPath, newPath);
            return false;
        }

        var torrent = this.torrentRepository?.Get(torrentId);
        if (torrent != null && !string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            var targetFullPath = Path.Combine(torrent.SavePath, newPath.Replace('\\', '/').Trim('/'));
            if (!TorrentPathValidator.IsStrictSubPath(torrent.SavePath, targetFullPath))
            {
                this.logger.Warn("Refusing to rename folder in torrent {0}: target path '{1}' escapes save path '{2}'", torrentId, newPath, torrent.SavePath);
                return false;
            }
        }

        var result = await this.downloadEngine.RenameFolderAsync(torrentId, oldPath, newPath);
        if (result && this.fileRepository != null)
        {
            var normalizedOld = oldPath.Replace('\\', '/').Trim('/');
            var normalizedNew = newPath.Replace('\\', '/').Trim('/');
            var dbFiles = this.fileRepository.GetByTorrentId(torrentId);
            foreach (var file in dbFiles)
            {
                if (!string.IsNullOrEmpty(file.Path))
                {
                    var cur = file.Path.Replace('\\', '/').TrimStart('/');
                    if (cur.StartsWith(normalizedOld + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        var sub = cur[(normalizedOld.Length + 1)..];
                        file.Path = $"{normalizedNew}/{sub}";
                        this.fileRepository.Update(file);
                    }
                }
            }
        }

        return result;
    }

    public async Task SetSuperSeedingAsync(int id, bool enabled)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null)
        {
            torrent.InitialSeeding = enabled;
            this.torrentRepository.Update(torrent);
            await this.downloadEngine.SetSuperSeedingAsync(id, enabled);
            this.logger.Info("Set super seeding for torrent {0} ({1}): {2}", id, torrent.Name, enabled);
        }
    }

    public async Task SetLocationAsync(int id, string newSavePath, bool moveFiles = true)
    {
        if (string.IsNullOrWhiteSpace(newSavePath))
        {
            throw new ArgumentException("New save path must not be empty.", nameof(newSavePath));
        }

        var torrent = this.torrentRepository.Get(id);
        if (torrent == null)
        {
            this.logger.Warn("Cannot set location for torrent id {0}: torrent not found", id);
            return;
        }

        var oldSavePath = torrent.SavePath;
        if (string.Equals(oldSavePath, newSavePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (this.downloadEngine != null)
        {
            this.logger.Info("Setting location for torrent {0} ({1}) from '{2}' to '{3}' (moveFiles={4})", torrent.Id, torrent.Name, oldSavePath, newSavePath, moveFiles);
            try
            {
                await this.downloadEngine.MoveTorrentFilesAsync(id, newSavePath, moveFiles);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to move files in download engine for torrent {0} to '{1}'", torrent.Id, newSavePath);
                throw;
            }
        }

        torrent.SavePath = newSavePath;
        this.torrentRepository.Update(torrent);
        this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = torrent });
        this.logger.Info("Updated save path for torrent {0} ({1}) to '{2}' (moved={3})", torrent.Id, torrent.Name, newSavePath, moveFiles);
    }

    public async Task SetCategoryAsync(int id, string category)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent == null)
        {
            this.logger.Warn("Cannot set category for torrent id {0}: torrent not found", id);
            return;
        }

        torrent.Category = category ?? string.Empty;

        var cat = !string.IsNullOrWhiteSpace(category)
            ? this.categoryService.GetByName(category)
            : null;

        if (cat != null)
        {
            if (torrent.TargetRatio <= 0)
            {
                torrent.TargetRatio = cat.TargetRatio;
            }

            if (torrent.TargetSeedTimeMinutes <= 0)
            {
                torrent.TargetSeedTimeMinutes = cat.TargetSeedTimeMinutes;
            }
        }

        var effectiveDl = this.GetEffectiveDownloadLimit(torrent);
        var effectiveUl = this.GetEffectiveUploadLimit(torrent);

        if (this.downloadEngine != null)
        {
            try
            {
                await this.downloadEngine.SetTorrentRateLimitsAsync(torrent.Id, effectiveDl, effectiveUl);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to apply category rate limits to download engine for torrent {0}", torrent.Id);
            }
        }

        this.torrentRepository.Update(torrent);
        this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = torrent });
        this.logger.Info("Updated category for torrent {0} ({1}) to '{2}'", torrent.Id, torrent.Name, category);
    }

    public int GetEffectiveDownloadLimit(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        var cat = !string.IsNullOrWhiteSpace(torrent.Category)
            ? this.categoryService.GetByName(torrent.Category)
            : null;
        var categoryLimit = cat?.DefaultDownloadLimit ?? 0;

        if (this.speedSchedulerService != null)
        {
            return this.speedSchedulerService.ResolveEffectiveDownloadLimit(torrent.DownloadLimit, categoryLimit);
        }

        return torrent.DownloadLimit > 0 ? torrent.DownloadLimit : categoryLimit;
    }

    public int GetEffectiveUploadLimit(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        var cat = !string.IsNullOrWhiteSpace(torrent.Category)
            ? this.categoryService.GetByName(torrent.Category)
            : null;
        var categoryLimit = cat?.DefaultUploadLimit ?? 0;

        if (this.speedSchedulerService != null)
        {
            return this.speedSchedulerService.ResolveEffectiveUploadLimit(torrent.UploadLimit, categoryLimit);
        }

        return torrent.UploadLimit > 0 ? torrent.UploadLimit : categoryLimit;
    }

    public async Task PropagateCategoryLimitsAsync(Category category)
    {
        if (category == null || string.IsNullOrWhiteSpace(category.Name))
        {
            return;
        }

        var torrents = this.torrentRepository.GetByCategory(category.Name);
        if (torrents == null)
        {
            return;
        }

        foreach (var torrent in torrents)
        {
            var effectiveDl = this.speedSchedulerService != null
                ? this.speedSchedulerService.ResolveEffectiveDownloadLimit(torrent.DownloadLimit, category.DefaultDownloadLimit)
                : (torrent.DownloadLimit > 0 ? torrent.DownloadLimit : category.DefaultDownloadLimit);

            var effectiveUl = this.speedSchedulerService != null
                ? this.speedSchedulerService.ResolveEffectiveUploadLimit(torrent.UploadLimit, category.DefaultUploadLimit)
                : (torrent.UploadLimit > 0 ? torrent.UploadLimit : category.DefaultUploadLimit);

            if (this.downloadEngine != null)
            {
                try
                {
                    await this.downloadEngine.SetTorrentRateLimitsAsync(torrent.Id, effectiveDl, effectiveUl);
                    this.logger.Info(
                        "Propagated category limit updates for category '{0}' to torrent {1} ({2}) - DL: {3} KB/s, UL: {4} KB/s",
                        category.Name,
                        torrent.Id,
                        torrent.Name,
                        effectiveDl,
                        effectiveUl);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to propagate category rate limits to download engine for torrent {0}", torrent.Id);
                }
            }
        }
    }

    public async Task PropagateCategoryDeletedAsync(CategoryDeletedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var torrentsToUpdate = new List<Torrent>();

        if (message.AffectedTorrentIds != null && message.AffectedTorrentIds.Count > 0)
        {
            foreach (var id in message.AffectedTorrentIds)
            {
                var t = this.torrentRepository.Get(id);
                if (t != null)
                {
                    torrentsToUpdate.Add(t);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(message.CategoryName))
        {
            var torrents = this.torrentRepository.GetByCategory(message.CategoryName);
            if (torrents != null)
            {
                torrentsToUpdate.AddRange(torrents);
            }
        }

        foreach (var torrent in torrentsToUpdate)
        {
            var effectiveDl = this.speedSchedulerService != null
                ? this.speedSchedulerService.ResolveEffectiveDownloadLimit(torrent.DownloadLimit, 0)
                : (torrent.DownloadLimit > 0 ? torrent.DownloadLimit : 0);

            var effectiveUl = this.speedSchedulerService != null
                ? this.speedSchedulerService.ResolveEffectiveUploadLimit(torrent.UploadLimit, 0)
                : (torrent.UploadLimit > 0 ? torrent.UploadLimit : 0);

            if (this.downloadEngine != null)
            {
                try
                {
                    await this.downloadEngine.SetTorrentRateLimitsAsync(torrent.Id, effectiveDl, effectiveUl);
                    this.logger.Info(
                        "Propagated category deletion for category '{0}' to torrent {1} ({2}) - DL: {3} KB/s, UL: {4} KB/s",
                        message.CategoryName,
                        torrent.Id,
                        torrent.Name,
                        effectiveDl,
                        effectiveUl);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to propagate category deletion rate limits to download engine for torrent {0}", torrent.Id);
                }
            }
        }
    }

    public void Handle(CategoryUpdatedEvent message)
    {
        if (message?.Category == null || string.IsNullOrWhiteSpace(message.Category.Name))
        {
            return;
        }

        _ = this.PropagateCategoryLimitsAsync(message.Category);
    }

    public void Handle(CategoryDeletedEvent message)
    {
        if (message == null)
        {
            return;
        }

        _ = this.PropagateCategoryDeletedAsync(message);
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var torrent = this.torrentRepository.Get(message.Torrent.Id);
        if (torrent != null)
        {
            var oldStatus = torrent.Status;
            torrent.Status = TorrentStatus.Seeding;
            torrent.Progress = 1.0;
            torrent.DateCompleted = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(message.Torrent.SavePath))
            {
                torrent.SavePath = message.Torrent.SavePath;
            }

            this.torrentRepository.Update(torrent);
            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
            {
                Torrent = torrent,
                OldStatus = oldStatus,
                NewStatus = TorrentStatus.Seeding,
            });
        }
    }

    private sealed class RefCountedSemaphore
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; } = 1;
    }
}
