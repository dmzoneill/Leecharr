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

public class TorrentService : ITorrentService, IHandle<TorrentDownloadCompletedEvent>, IHandle<CategoryUpdatedEvent>, IHandle<CategoryDeletedEvent>, IHandle<TorrentMetadataReceivedEvent>
{
    private static readonly SemaphoreSlim QueueLock = new(1, 1);
    private readonly ConcurrentDictionary<int, long> lastSeenSessionUploaded = new();
    private readonly ConcurrentDictionary<int, long> sessionUploadBaselines = new();
    private readonly Dictionary<int, RefCountedSemaphore> deletionLocks = new();
    private readonly object deletionLocksSync = new();
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentFileRepository fileRepository;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;
    private readonly ICategoryService categoryService;
    private readonly IMediaEnrichmentService mediaEnrichmentService;
    private readonly IConfigService configService;
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

    public TorrentService(
        ITorrentRepository torrentRepository,
        ITorrentFileRepository fileRepository,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator,
        ITorrentServiceContext context)
        : this(
            torrentRepository,
            fileRepository,
            context?.CategoryService,
            context?.MediaEnrichmentService,
            context?.ConfigService,
            downloadEngine,
            eventAggregator,
            context?.TrackerEntryRepository,
            context?.QueueManagerService,
            context?.StoragePathService,
            context?.AppFolderInfo,
            context?.TorrentLogService,
            context?.SpeedSchedulerService)
    {
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
                QueueLock.Wait();
                try
                {
                    if (torrent.QueuePosition <= 0)
                    {
                        torrent.QueuePosition = this.torrentRepository.GetNextQueuePosition();
                        this.torrentRepository.Update(torrent);
                    }
                }
                finally
                {
                    QueueLock.Release();
                }
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

    public Task<Torrent> AddFromParsedTorrentAsync(
        ParsedTorrent parsed,
        string category = null,
        string savePath = null,
        bool startPaused = false,
        byte[] rawBytes = null)
    {
        return this.AddFromParsedTorrentAsync(parsed, category, savePath, startPaused, rawBytes, null, null);
    }

    public async Task<Torrent> AddFromParsedTorrentAsync(
        ParsedTorrent parsed,
        string category,
        string savePath,
        bool startPaused,
        byte[] rawBytes,
        bool? sequentialDownload,
        bool? firstLastPiecePriority)
    {
        if (parsed == null)
        {
            throw new ArgumentNullException(nameof(parsed));
        }

        var existing = this.GetByInfoHash(parsed.InfoHash) ?? (!string.IsNullOrWhiteSpace(parsed.V2InfoHash) ? this.GetByInfoHash(parsed.V2InfoHash) : null);
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

        var effectiveSavePath = this.ResolveEffectiveSavePath(savePath, effectiveCategory, defaultDownloadDir);

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash.ToLowerInvariant(),
            V2InfoHash = parsed.V2InfoHash?.ToLowerInvariant(),
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
            QueuePosition = 0,
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>(),
            SequentialDownload = sequentialDownload ?? string.Equals(this.configService?.PiecePickerStrategy, "Sequential", StringComparison.OrdinalIgnoreCase),
            FirstLastPiecePriority = firstLastPiecePriority ?? false,
        };

        Torrent inserted;
        await QueueLock.WaitAsync().ConfigureAwait(false);
        try
        {
            torrent.QueuePosition = this.torrentRepository.GetNextQueuePosition();
            inserted = this.torrentRepository.Insert(torrent) ?? torrent;
        }
        finally
        {
            QueueLock.Release();
        }

        // Insert torrent files
        if (parsed.Files != null)
        {
            var pieceLength = Math.Max(1, parsed.PieceLength);
            long runningByteOffset = 0;
            var torrentFiles = new List<TorrentFile>();
            foreach (var file in parsed.Files)
            {
                var effectiveByteOffset = file.ByteOffset > 0 ? file.ByteOffset : runningByteOffset;
                var startPiece = (int)(effectiveByteOffset / pieceLength);
                var endByte = effectiveByteOffset + file.Size - 1;
                var endPiece = file.Size > 0 ? (int)(endByte / pieceLength) : startPiece;
                var pieceCount = file.Size > 0 ? (endPiece - startPiece + 1) : 0;

                var torrentFile = new TorrentFile
                {
                    TorrentId = inserted.Id,
                    Path = file.Path,
                    Size = file.Size,
                    PieceOffset = startPiece,
                    PieceCount = pieceCount,
                    ByteOffset = effectiveByteOffset,
                    Priority = 3,
                    Progress = 0.0,
                };
                torrentFiles.Add(torrentFile);
                runningByteOffset = effectiveByteOffset + file.Size;
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

            if (this.downloadEngine != null && inserted.SequentialDownload)
            {
                await this.downloadEngine.SetSequentialDownloadAsync(inserted.Id, true).ConfigureAwait(false);
            }

            if (this.downloadEngine != null && inserted.FirstLastPiecePriority)
            {
                await this.downloadEngine.SetFirstLastPiecePriorityAsync(inserted.Id, true).ConfigureAwait(false);
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

    public Task<Torrent> AddFromMagnetAsync(
        string magnetUri,
        string category = null,
        string savePath = null,
        bool startPaused = false)
    {
        return this.AddFromMagnetAsync(magnetUri, category, savePath, startPaused, null, null);
    }

    public async Task<Torrent> AddFromMagnetAsync(
        string magnetUri,
        string category,
        string savePath,
        bool startPaused,
        bool? sequentialDownload,
        bool? firstLastPiecePriority)
    {
        var parsedMagnet = MagnetLinkParser.Parse(magnetUri);
        var existing = this.GetByInfoHash(parsedMagnet?.InfoHash) ?? (!string.IsNullOrWhiteSpace(parsedMagnet?.V2InfoHash) ? this.GetByInfoHash(parsedMagnet.V2InfoHash) : null);
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

        var effectiveSavePath = this.ResolveEffectiveSavePath(savePath, effectiveCategory, defaultDownloadDir);

        var torrent = new Torrent
        {
            Name = !string.IsNullOrWhiteSpace(parsedMagnet.DisplayName) ? parsedMagnet.DisplayName : parsedMagnet.InfoHash,
            InfoHash = parsedMagnet.InfoHash.ToLowerInvariant(),
            V2InfoHash = parsedMagnet.V2InfoHash?.ToLowerInvariant(),
            TotalSize = 0,
            PieceCount = 0,
            PieceLength = 0,
            TrackerUrl = parsedMagnet.Trackers?.FirstOrDefault(),
            Status = startPaused ? TorrentStatus.Paused : TorrentStatus.Downloading,
            Category = effectiveCategory,
            SavePath = effectiveSavePath,
            QueuePosition = 0,
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>(),
            SequentialDownload = sequentialDownload ?? string.Equals(this.configService?.PiecePickerStrategy, "Sequential", StringComparison.OrdinalIgnoreCase),
            FirstLastPiecePriority = firstLastPiecePriority ?? false,
        };

        Torrent inserted;
        await QueueLock.WaitAsync().ConfigureAwait(false);
        try
        {
            torrent.QueuePosition = this.torrentRepository.GetNextQueuePosition();
            inserted = this.torrentRepository.Insert(torrent) ?? torrent;
        }
        finally
        {
            QueueLock.Release();
        }

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

            if (this.downloadEngine != null && inserted.SequentialDownload)
            {
                await this.downloadEngine.SetSequentialDownloadAsync(inserted.Id, true).ConfigureAwait(false);
            }

            if (this.downloadEngine != null && inserted.FirstLastPiecePriority)
            {
                await this.downloadEngine.SetFirstLastPiecePriorityAsync(inserted.Id, true).ConfigureAwait(false);
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

            try
            {
                await this.downloadEngine.SetTorrentCategoryAsync(torrent.Id, torrent.Category);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to apply category to download engine for torrent {0}", torrent.Id);
            }

            if (existing == null || existing.SequentialDownload != torrent.SequentialDownload)
            {
                try
                {
                    await this.downloadEngine.SetSequentialDownloadAsync(torrent.Id, torrent.SequentialDownload).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to apply sequential download to download engine for torrent {0}", torrent.Id);
                }
            }

            if (existing == null || existing.FirstLastPiecePriority != torrent.FirstLastPiecePriority)
            {
                try
                {
                    await this.downloadEngine.SetFirstLastPiecePriorityAsync(torrent.Id, torrent.FirstLastPiecePriority).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to apply first/last piece priority to download engine for torrent {0}", torrent.Id);
                }
            }
        }

        var updated = this.torrentRepository.Update(torrent);
        this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = updated });
        return await Task.FromResult(updated);
    }

    public async Task SetSequentialDownloadAsync(int id, bool enabled)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null)
        {
            torrent.SequentialDownload = enabled;
            this.torrentRepository.Update(torrent);
            this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = torrent });
        }

        if (this.downloadEngine != null)
        {
            await this.downloadEngine.SetSequentialDownloadAsync(id, enabled).ConfigureAwait(false);
        }
    }

    public async Task SetFirstLastPiecePriorityAsync(int id, bool enabled)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null)
        {
            torrent.FirstLastPiecePriority = enabled;
            this.torrentRepository.Update(torrent);
            this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = torrent });
        }

        if (this.downloadEngine != null)
        {
            await this.downloadEngine.SetFirstLastPiecePriorityAsync(id, enabled).ConfigureAwait(false);
        }
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

            this.eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = deleteFiles });

            // Note: Cached .torrent files in AppDataFolder/Torrents are preserved for Download History
            // so historical torrents can be losslessly re-added. They are cleaned up when history records are deleted.
            if (deleteFiles)
            {
                await this.DeleteTorrentDataOnDiskAsync(torrent, torrentFiles);
            }

            this.fileRepository.DeleteByTorrentId(id);
            this.trackerEntryRepository?.DeleteByTorrentId(id);
            this.mediaEnrichmentService.DeleteMetadata(id);
            this.mediaEnrichmentService.CleanupTorrentCache(id);
            this.sessionUploadBaselines.TryRemove(id, out _);
            this.lastSeenSessionUploaded.TryRemove(id, out _);
            this.torrentRepository.Delete(id);
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

    public Task PauseAsync(int id)
    {
        return this.PauseAsync(id, null);
    }

    public async Task PauseAsync(int id, string reason)
    {
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null && torrent.Status != TorrentStatus.Paused)
        {
            var old = torrent.Status;
            var reasonSuffix = !string.IsNullOrWhiteSpace(reason) ? $" [Reason: {reason}]" : string.Empty;
            this.logger.Info("[State Machine] Torrent #{0} ('{1}') pause requested (Status: {2} -> Paused){3}", id, torrent.Name, old, reasonSuffix);
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

            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
            {
                Torrent = torrent,
                OldStatus = old,
                NewStatus = TorrentStatus.Paused,
                Reason = reason,
            });
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
            this.logger.Info("[State Machine] Torrent #{0} ('{1}') resume requested (Status: {2} -> {3}, Progress: {4:P1})", id, torrent.Name, old, newStatus, torrent.Progress);
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
            this.logger.Info("[State Machine] Torrent #{0} ('{1}') force recheck requested (Status: {2} -> Checking)", id, torrent.Name, old);
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

            var task = this.downloadEngine.GetTask(id);
            var newStatus = task?.Status == TorrentStatus.QueuedForChecking
                ? TorrentStatus.QueuedForChecking
                : TorrentStatus.Checking;
            torrent.Status = newStatus;
            this.torrentRepository.Update(torrent);
            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = newStatus });
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

            var posStr = position?.ToLowerInvariant();
            if (int.TryParse(posStr, out var targetIndex))
            {
                var clampedIndex = Math.Clamp(targetIndex, 0, allTorrents.Count);
                allTorrents.Insert(clampedIndex, torrent);
            }
            else
            {
                switch (posStr)
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

    public async Task MoveQueueBatchAsync(IEnumerable<int> ids, string direction)
    {
        if (ids == null)
        {
            return;
        }

        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await QueueLock.WaitAsync();
        try
        {
            var allTorrents = this.torrentRepository.All().OrderBy(t => t.QueuePosition).ToList();
            if (allTorrents.Count == 0)
            {
                return;
            }

            var targetSet = new HashSet<int>(idList);
            var targetTorrents = allTorrents.Where(t => targetSet.Contains(t.Id)).ToList();
            if (targetTorrents.Count == 0)
            {
                return;
            }

            var dir = direction?.ToLowerInvariant();
            if (dir == "up")
            {
                var limit = 0;
                foreach (var target in targetTorrents)
                {
                    var currentIndex = allTorrents.IndexOf(target);
                    if (currentIndex < 0)
                    {
                        continue;
                    }

                    if (currentIndex <= limit)
                    {
                        limit = limit + 1;
                    }
                    else
                    {
                        var targetIndex = currentIndex - 1;
                        allTorrents.RemoveAt(currentIndex);
                        allTorrents.Insert(targetIndex, target);
                        if (targetIndex == limit)
                        {
                            limit = limit + 1;
                        }
                    }
                }
            }
            else if (dir == "down")
            {
                var limit = allTorrents.Count - 1;
                for (var i = targetTorrents.Count - 1; i >= 0; i--)
                {
                    var target = targetTorrents[i];
                    var currentIndex = allTorrents.IndexOf(target);
                    if (currentIndex < 0)
                    {
                        continue;
                    }

                    if (currentIndex >= limit)
                    {
                        limit = limit - 1;
                    }
                    else
                    {
                        var targetIndex = currentIndex + 1;
                        allTorrents.RemoveAt(currentIndex);
                        allTorrents.Insert(targetIndex, target);
                        if (targetIndex == limit)
                        {
                            limit = limit - 1;
                        }
                    }
                }
            }
            else if (dir == "top")
            {
                for (var i = 0; i < targetTorrents.Count; i++)
                {
                    allTorrents.Remove(targetTorrents[i]);
                    allTorrents.Insert(i, targetTorrents[i]);
                }
            }
            else if (dir == "bottom")
            {
                foreach (var target in targetTorrents)
                {
                    allTorrents.Remove(target);
                    allTorrents.Add(target);
                }
            }
            else
            {
                return;
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
            var oldDownloaded = torrent.Downloaded;
            var oldUploaded = torrent.Uploaded;
            var oldRatio = torrent.Ratio;
            var oldProgress = torrent.Progress;

            torrent.Status = task.Status;
            torrent.ErrorMessage = task.ErrorMessage;
            torrent.Progress = task.Progress;

            var initialSeedingConcluded = torrent.InitialSeeding && !task.IsSuperSeeding && task.Status == TorrentStatus.Seeding;
            var initialSeedingChanged = torrent.InitialSeeding != task.IsSuperSeeding;
            if (initialSeedingChanged)
            {
                torrent.InitialSeeding = task.IsSuperSeeding;
            }

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
            if (currentSessionUploaded >= 0)
            {
                if (this.lastSeenSessionUploaded.TryGetValue(torrent.Id, out var lastSeen) && currentSessionUploaded < lastSeen)
                {
                    // Active engine session was reset or restarted
                    this.sessionUploadBaselines[torrent.Id] = torrent.Uploaded;
                }
                else if (!this.sessionUploadBaselines.ContainsKey(torrent.Id))
                {
                    // If the task already reports total lifetime upload matching the torrent model,
                    // the task uploaded count is already cumulative (e.g. Transmission/LibTorrent engines).
                    // Otherwise, the task reports session bytes since startup (e.g. MonoTorrent), so baseline is the DB cumulative upload.
                    var baseline = currentSessionUploaded == torrent.Uploaded && currentSessionUploaded > 0
                        ? 0
                        : torrent.Uploaded;

                    this.sessionUploadBaselines.TryAdd(torrent.Id, baseline);
                }

                this.lastSeenSessionUploaded[torrent.Id] = currentSessionUploaded;

                var sessionBaseline = this.sessionUploadBaselines.TryGetValue(torrent.Id, out var b) ? b : torrent.Uploaded;
                torrent.Uploaded = Math.Max(torrent.Uploaded, sessionBaseline + currentSessionUploaded);
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

            var dateCompletedSet = false;
            // Record completion timestamp when torrent reaches Seeding
            if (torrent.Status == TorrentStatus.Seeding && !torrent.DateCompleted.HasValue)
            {
                torrent.DateCompleted = DateTime.UtcNow;
                dateCompletedSet = true;
            }

            var statusChanged = oldStatus != torrent.Status;
            var progressChanged = Math.Abs(torrent.Progress - oldProgress) > 0.0001;
            var statsChanged = torrent.Uploaded != oldUploaded ||
                               torrent.Downloaded != oldDownloaded ||
                               Math.Abs(torrent.Ratio - oldRatio) > 0.0001 ||
                               progressChanged;

            if (statusChanged)
            {
                this.logger.Info(
                    "[State Machine] Torrent #{0} ('{1}') status updated in engine sync: {2} -> {3} (Progress: {4:P1}, DownSpeed: {5}/s, UpSpeed: {6}/s)",
                    torrent.Id,
                    torrent.Name,
                    oldStatus,
                    torrent.Status,
                    torrent.Progress,
                    torrent.DownloadSpeed,
                    torrent.UploadSpeed);

                this.torrentRepository.Update(torrent);
                this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                {
                    Torrent = torrent,
                    OldStatus = oldStatus,
                    NewStatus = torrent.Status,
                });
            }
            else if (initialSeedingConcluded)
            {
                this.torrentRepository.Update(torrent);
                this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = torrent });
                this.logger.Info("Initial seeding (super seeding) concluded for torrent {0} ({1}); synchronized to normal seeding.", torrent.Id, torrent.Name);
            }
            else if (initialSeedingChanged || dateCompletedSet || statsChanged)
            {
                this.torrentRepository.Update(torrent);
            }
        }
        else
        {
            this.sessionUploadBaselines.TryRemove(torrent.Id, out _);
            this.lastSeenSessionUploaded.TryRemove(torrent.Id, out _);

            if (torrent.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued)
            {
                torrent.DownloadSpeed = 0;
                torrent.UploadSpeed = 0;
                torrent.Eta = 0;
                torrent.Seeders = 0;
                torrent.Leechers = 0;
            }
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
            if (enabled && (torrent.Progress < 1.0 || torrent.Status != TorrentStatus.Seeding))
            {
                throw new InvalidOperationException("Super seeding can only be enabled for 100% completed seeding torrents.");
            }

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
                if (moveFiles && !string.IsNullOrWhiteSpace(oldSavePath))
                {
                    try
                    {
                        this.logger.Info("Attempting to rollback torrent {0} files back to '{1}'", torrent.Id, oldSavePath);
                        await this.downloadEngine.MoveTorrentFilesAsync(id, oldSavePath, moveFiles: true);
                    }
                    catch (Exception rollbackEx)
                    {
                        this.logger.Error(rollbackEx, "Failed to rollback torrent {0} files to '{1}' after move failure", torrent.Id, oldSavePath);
                    }
                }

                throw;
            }
        }

        torrent.SavePath = newSavePath;
        try
        {
            this.torrentRepository.Update(torrent);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to update repository for torrent {0} after moving files to '{1}'", torrent.Id, newSavePath);
            torrent.SavePath = oldSavePath;
            if (this.downloadEngine != null && !string.IsNullOrWhiteSpace(oldSavePath))
            {
                try
                {
                    this.logger.Info("Attempting to rollback torrent {0} files back to '{1}' after database update failure", torrent.Id, oldSavePath);
                    await this.downloadEngine.MoveTorrentFilesAsync(id, oldSavePath, moveFiles);
                }
                catch (Exception rollbackEx)
                {
                    this.logger.Error(rollbackEx, "Failed to rollback torrent {0} files to '{1}' after database update failure", torrent.Id, oldSavePath);
                }
            }

            throw;
        }

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

            try
            {
                await this.downloadEngine.SetTorrentCategoryAsync(torrent.Id, torrent.Category);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to apply category to download engine for torrent {0}", torrent.Id);
            }
        }

        this.torrentRepository.Update(torrent);
        this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = torrent });
        this.logger.Info("Updated category for torrent {0} ({1}) to '{2}'", torrent.Id, torrent.Name, category);
    }

    public int ResolveEffectiveDownloadLimit(int torrentLimit, int categoryLimit)
    {
        if (this.speedSchedulerService != null)
        {
            return this.speedSchedulerService.ResolveEffectiveDownloadLimit(torrentLimit, categoryLimit);
        }

        if (torrentLimit > 0)
        {
            return torrentLimit;
        }

        if (categoryLimit > 0)
        {
            return categoryLimit;
        }

        return 0;
    }

    public int ResolveEffectiveUploadLimit(int torrentLimit, int categoryLimit)
    {
        if (this.speedSchedulerService != null)
        {
            return this.speedSchedulerService.ResolveEffectiveUploadLimit(torrentLimit, categoryLimit);
        }

        if (torrentLimit > 0)
        {
            return torrentLimit;
        }

        if (categoryLimit > 0)
        {
            return categoryLimit;
        }

        return 0;
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

        return this.ResolveEffectiveDownloadLimit(torrent.DownloadLimit, categoryLimit);
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

        return this.ResolveEffectiveUploadLimit(torrent.UploadLimit, categoryLimit);
    }

    public double GetEffectiveTargetRatio(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        if (torrent.TargetRatio > 0)
        {
            return torrent.TargetRatio;
        }

        var cat = !string.IsNullOrWhiteSpace(torrent.Category)
            ? this.categoryService?.GetByName(torrent.Category)
            : null;

        if (cat != null && cat.TargetRatio > 0)
        {
            return cat.TargetRatio;
        }

        return this.configService?.GlobalSeedRatioLimit ?? 0;
    }

    public int GetEffectiveTargetSeedTimeMinutes(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        if (torrent.TargetSeedTimeMinutes > 0)
        {
            return torrent.TargetSeedTimeMinutes;
        }

        var cat = !string.IsNullOrWhiteSpace(torrent.Category)
            ? this.categoryService?.GetByName(torrent.Category)
            : null;

        if (cat != null && cat.TargetSeedTimeMinutes > 0)
        {
            return cat.TargetSeedTimeMinutes;
        }

        return 0;
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
            var effectiveDl = this.ResolveEffectiveDownloadLimit(torrent.DownloadLimit, category.DefaultDownloadLimit);
            var effectiveUl = this.ResolveEffectiveUploadLimit(torrent.UploadLimit, category.DefaultUploadLimit);

            if (this.downloadEngine != null)
            {
                try
                {
                    await this.downloadEngine.SetTorrentRateLimitsAsync(torrent.Id, effectiveDl, effectiveUl);
                    await this.downloadEngine.SetTorrentCategoryAsync(torrent.Id, torrent.Category);
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
            var effectiveDl = this.ResolveEffectiveDownloadLimit(torrent.DownloadLimit, 0);
            var effectiveUl = this.ResolveEffectiveUploadLimit(torrent.UploadLimit, 0);

            if (this.downloadEngine != null)
            {
                try
                {
                    await this.downloadEngine.SetTorrentRateLimitsAsync(torrent.Id, effectiveDl, effectiveUl);
                    await this.downloadEngine.SetTorrentCategoryAsync(torrent.Id, torrent.Category);
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

    public void Handle(TorrentMetadataReceivedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var torrent = this.torrentRepository.Get(message.TorrentId)
            ?? (!string.IsNullOrWhiteSpace(message.InfoHash) ? this.torrentRepository.GetByInfoHash(message.InfoHash) : null);

        if (torrent == null)
        {
            return;
        }

        var updated = false;
        if (!string.IsNullOrWhiteSpace(message.Name) && (string.Equals(torrent.Name, torrent.InfoHash, StringComparison.OrdinalIgnoreCase) || torrent.TotalSize == 0))
        {
            torrent.Name = message.Name;
            updated = true;
        }

        if (message.TotalSize > 0 && torrent.TotalSize != message.TotalSize)
        {
            torrent.TotalSize = message.TotalSize;
            updated = true;
        }

        if (message.PieceLength > 0 && torrent.PieceLength != message.PieceLength)
        {
            torrent.PieceLength = message.PieceLength;
            updated = true;
        }

        if (message.PieceCount > 0 && torrent.PieceCount != message.PieceCount)
        {
            torrent.PieceCount = message.PieceCount;
            updated = true;
        }

        if (updated)
        {
            this.torrentRepository.Update(torrent);
            this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = torrent });
        }

        // Insert torrent files if none exist yet
        if (message.Files != null && message.Files.Count > 0 && this.fileRepository != null)
        {
            var existingFiles = this.fileRepository.GetByTorrentId(torrent.Id);
            if (existingFiles == null || !existingFiles.Any())
            {
                this.fileRepository.InsertMany(message.Files.ToList());
            }
        }

        // Trigger media enrichment with resolved name if auto enrich is enabled
        if (this.configService.AutoEnrichEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.mediaEnrichmentService.EnrichTorrentAsync(torrent);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Background media enrichment failed for resolved magnet {0}", torrent.Id);
                }
            });
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var torrent = this.torrentRepository.Get(message.Torrent.Id);
        if (torrent == null)
        {
            return;
        }

        var category = !string.IsNullOrWhiteSpace(torrent.Category)
            ? this.categoryService?.GetByName(torrent.Category)
            : this.categoryService?.GetByName(string.Empty);

        var oldStatus = torrent.Status;
        torrent.Progress = 1.0;
        torrent.DateCompleted = DateTime.UtcNow;

        var completedDir = this.storagePathService?.GetCompletedDirectory(torrent.Category);
        if (string.IsNullOrWhiteSpace(completedDir))
        {
            completedDir = this.configService?.DownloadDir;
        }

        if (string.IsNullOrWhiteSpace(completedDir))
        {
            completedDir = "/downloads";
        }

        var targetSavePath = message.Torrent.SavePath;
        if (!string.IsNullOrWhiteSpace(targetSavePath) && this.storagePathService != null)
        {
            var norm = this.storagePathService.NormalizeCompletedSavePath(targetSavePath, torrent.Category);
            if (!string.IsNullOrWhiteSpace(norm))
            {
                targetSavePath = norm;
            }
        }

        if (string.IsNullOrWhiteSpace(targetSavePath) ||
            string.Equals(targetSavePath.TrimEnd('/', '\\'), "/downloads/incomplete", StringComparison.OrdinalIgnoreCase))
        {
            targetSavePath = completedDir;
        }

        torrent.SavePath = targetSavePath;

        if (this.torrentRepository.Get(torrent.Id) == null)
        {
            this.logger.Debug("Torrent {0} was removed before download completion could be saved; ignoring update.", torrent.Id);
            return;
        }

        if (category?.AutoStop == true)
        {
            torrent.Status = TorrentStatus.Paused;
            torrent.DownloadSpeed = 0;
            torrent.UploadSpeed = 0;
            torrent.Eta = 0;
            torrent.Seeders = 0;
            torrent.Leechers = 0;
            this.torrentRepository.Update(torrent);

            try
            {
                _ = this.downloadEngine?.PauseTorrentAsync(torrent.Id);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error pausing torrent for AutoStop category on download completion: {0}", torrent.Id);
            }

            var autoStopReason = $"Auto-stopped upon download completion per category '{category?.Name ?? "Default"}' AutoStop setting";
            this.logger.Info("[State Machine] Torrent #{0} ('{1}') auto-stopped upon download completion per category '{2}' setting.", torrent.Id, torrent.Name, category?.Name);
            this.torrentLogService?.Log(torrent.Id, "Info", "Engine", autoStopReason);

            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
            {
                Torrent = torrent,
                OldStatus = oldStatus,
                NewStatus = TorrentStatus.Paused,
                Reason = autoStopReason,
            });
        }
        else
        {
            torrent.Status = TorrentStatus.Seeding;
            torrent.DownloadSpeed = 0;
            torrent.Eta = 0;
            this.torrentRepository.Update(torrent);
            this.logger.Info("[State Machine] Torrent #{0} ('{1}') download completed (100% verified); transitioned to Seeding.", torrent.Id, torrent.Name);
            this.torrentLogService?.Log(torrent.Id, "Info", "Engine", "Download completed; entered Seeding state");
            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
            {
                Torrent = torrent,
                OldStatus = oldStatus,
                NewStatus = TorrentStatus.Seeding,
                Reason = "Download completed (100% verified)",
            });
        }
    }

    private string ResolveEffectiveSavePath(string savePath, string category, string defaultDownloadDir)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return defaultDownloadDir;
        }

        var normalizedPath = savePath.Replace('\\', '/').Trim();

        if (this.storagePathService != null)
        {
            var normalized = this.storagePathService.NormalizeCompletedSavePath(normalizedPath, category);
            if (!string.IsNullOrWhiteSpace(normalized) && !string.Equals(normalized, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalized.Replace('\\', '/').Trim();
            }
        }
        else if (string.Equals(normalizedPath.TrimEnd('/'), "/downloads/incomplete", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = defaultDownloadDir.Replace('\\', '/').Trim();
        }

        var allowedRoots = this.GetAllowedDownloadDirectories(category, defaultDownloadDir);

        var isRooted = Path.IsPathRooted(normalizedPath) ||
                       (normalizedPath.Length >= 2 && char.IsLetter(normalizedPath[0]) && normalizedPath[1] == ':');

        if (isRooted)
        {
            if (this.IsPathAllowed(normalizedPath, allowedRoots))
            {
                return normalizedPath;
            }

            var relativeSegments = normalizedPath.TrimStart('/');
            if (!string.IsNullOrWhiteSpace(category) && string.Equals(relativeSegments, category, StringComparison.OrdinalIgnoreCase))
            {
                return defaultDownloadDir;
            }

            if (!CategoryService.IsRootOrSystemDirectory(normalizedPath))
            {
                return normalizedPath;
            }

            this.logger.Warn("Save path '{0}' is outside allowed media directories. Falling back to '{1}'", savePath, defaultDownloadDir);
            return defaultDownloadDir;
        }

        var cleanRel = normalizedPath.TrimStart('/');
        if (!string.IsNullOrWhiteSpace(category) && string.Equals(cleanRel, category, StringComparison.OrdinalIgnoreCase))
        {
            return defaultDownloadDir;
        }

        var combined = Path.Combine(defaultDownloadDir, cleanRel);
        if (this.IsPathAllowed(combined, allowedRoots))
        {
            return combined;
        }

        this.logger.Warn("Relative save path '{0}' resolved outside allowed media directories. Falling back to '{1}'", savePath, defaultDownloadDir);
        return defaultDownloadDir;
    }

    private List<string> GetAllowedDownloadDirectories(string category, string defaultDownloadDir)
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(defaultDownloadDir))
        {
            roots.Add(defaultDownloadDir);
        }

        if (this.storagePathService != null)
        {
            try
            {
                var completedCat = this.storagePathService.GetCompletedDirectory(category);
                if (!string.IsNullOrWhiteSpace(completedCat))
                {
                    roots.Add(completedCat);
                }
            }
            catch
            {
            }

            try
            {
                var completedDefault = this.storagePathService.GetCompletedDirectory(null);
                if (!string.IsNullOrWhiteSpace(completedDefault))
                {
                    roots.Add(completedDefault);
                }
            }
            catch
            {
            }
        }

        if (this.categoryService != null)
        {
            try
            {
                var catSavePath = this.categoryService.GetSavePathForCategory(category);
                if (!string.IsNullOrWhiteSpace(catSavePath))
                {
                    roots.Add(catSavePath);
                }
            }
            catch
            {
            }

            try
            {
                var allCategories = this.categoryService.GetAll();
                if (allCategories != null)
                {
                    foreach (var cat in allCategories)
                    {
                        if (cat != null && !string.IsNullOrWhiteSpace(cat.SavePath))
                        {
                            roots.Add(cat.SavePath);
                        }

                        if (this.storagePathService != null && cat != null && !string.IsNullOrWhiteSpace(cat.Name))
                        {
                            try
                            {
                                var dir = this.storagePathService.GetCompletedDirectory(cat.Name);
                                if (!string.IsNullOrWhiteSpace(dir))
                                {
                                    roots.Add(dir);
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        if (this.configService != null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(this.configService.DownloadDir))
                {
                    roots.Add(this.configService.DownloadDir);
                }
            }
            catch
            {
            }
        }

        return roots;
    }

    private bool IsPathAllowed(string candidatePath, IEnumerable<string> allowedRoots)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        string canonicalCandidate;
        try
        {
            canonicalCandidate = TorrentPathValidator.ResolveCanonicalPath(candidatePath.Replace('\\', '/'));
        }
        catch
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var trimmedCandidate = canonicalCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string canonicalRoot;
            try
            {
                canonicalRoot = TorrentPathValidator.ResolveCanonicalPath(root);
            }
            catch
            {
                continue;
            }

            var trimmedRoot = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(trimmedCandidate, trimmedRoot, comparison))
            {
                return true;
            }

            if (TorrentPathValidator.IsStrictSubPath(canonicalRoot, canonicalCandidate))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class RefCountedSemaphore
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; } = 1;
    }
}
