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
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Torrents;

public class TorrentService : ITorrentService, IHandle<TorrentDownloadCompletedEvent>
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> deletionLocks = new ConcurrentDictionary<int, SemaphoreSlim>();
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
        IAppFolderInfo appFolderInfo = null)
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
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<Torrent> GetAll()
    {
        var torrents = this.torrentRepository.All().OrderBy(t => t.QueuePosition > 0 ? t.QueuePosition : t.Id).ToList();
        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];
            this.SyncWithEngine(torrent);
            if (torrent.QueuePosition <= 0)
            {
                torrent.QueuePosition = i + 1;
                this.torrentRepository.Update(torrent);
            }
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
                var maxPos = this.torrentRepository.All().Select(t => t.QueuePosition).DefaultIfEmpty(0).Max();
                torrent.QueuePosition = maxPos + 1;
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

        var existing = this.torrentRepository.GetByInfoHash(parsed.InfoHash);
        if (existing != null)
        {
            this.logger.Warn("Torrent with infohash {0} already exists", parsed.InfoHash);
            return existing;
        }

        var effectiveCategory = !string.IsNullOrWhiteSpace(category) ? category : this.configService.DefaultCategory;
        var effectiveSavePath = !string.IsNullOrWhiteSpace(savePath)
            ? savePath
            : this.categoryService.GetSavePathForCategory(effectiveCategory, this.configService.DownloadDir ?? "/downloads");

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
            QueuePosition = this.torrentRepository.All().Select(t => t.QueuePosition).DefaultIfEmpty(0).Max() + 1,
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>(),
        };

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
                    Priority = 1,
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

        // Insert trackers
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
                                AnnounceInterval = 1800,
                                LastAnnounce = null,
                                NextAnnounce = inserted.DateAdded.AddSeconds(1800),
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
                    AnnounceInterval = 1800,
                    LastAnnounce = null,
                    NextAnnounce = inserted.DateAdded.AddSeconds(1800),
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
        var existing = this.torrentRepository.GetByInfoHash(parsedMagnet.InfoHash);
        if (existing != null)
        {
            this.logger.Warn("Torrent with infohash {0} already exists", parsedMagnet.InfoHash);
            return existing;
        }

        var effectiveCategory = !string.IsNullOrWhiteSpace(category) ? category : this.configService.DefaultCategory;
        var effectiveSavePath = !string.IsNullOrWhiteSpace(savePath)
            ? savePath
            : this.categoryService.GetSavePathForCategory(effectiveCategory, this.configService.DownloadDir ?? "/downloads");

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
            QueuePosition = this.torrentRepository.All().Select(t => t.QueuePosition).DefaultIfEmpty(0).Max() + 1,
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>(),
        };

        var inserted = this.torrentRepository.Insert(torrent) ?? torrent;

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
                        AnnounceInterval = 1800,
                        LastAnnounce = null,
                        NextAnnounce = inserted.DateAdded.AddSeconds(1800),
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

        var updated = this.torrentRepository.Update(torrent);
        this.eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = updated });
        return await Task.FromResult(updated);
    }

    public async Task DeleteAsync(int id, bool deleteFiles = false)
    {
        var semaphore = this.deletionLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            var torrent = this.torrentRepository.Get(id);
            if (torrent == null)
            {
                return;
            }

            this.logger.Info("Deleting torrent {0} (DeleteFiles={1})", torrent.Name, deleteFiles);

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
                var isIncomplete = torrent.Progress < 1.0 ||
                                   torrent.Status == TorrentStatus.Downloading ||
                                   torrent.Status == TorrentStatus.Queued;

                if (isIncomplete)
                {
                    await this.PurgeIncompleteChunksAsync(torrent);
                }

                if (!string.IsNullOrWhiteSpace(torrent.SavePath) && !string.IsNullOrWhiteSpace(torrent.Name) && Directory.Exists(torrent.SavePath))
                {
                    try
                    {
                        var torrentFolder = Path.Combine(torrent.SavePath, torrent.Name);
                        if (!TorrentPathValidator.IsStrictSubPath(torrent.SavePath, torrentFolder))
                        {
                            this.logger.Warn("Refusing to delete files for torrent {0}: target path '{1}' escapes or equals save path '{2}'", torrent.Name, torrentFolder, torrent.SavePath);
                        }
                        else if (Directory.Exists(torrentFolder))
                        {
                            await DeletePathWithRetryAsync(torrentFolder, isDirectory: true);
                        }
                        else if (File.Exists(torrentFolder))
                        {
                            await DeletePathWithRetryAsync(torrentFolder, isDirectory: false);
                        }

                        if (isIncomplete)
                        {
                            var candidateExtensions = new[] { ".!mt", ".!leech", this.configService?.IncompleteExtension };
                            foreach (var ext in candidateExtensions)
                            {
                                if (string.IsNullOrWhiteSpace(ext))
                                {
                                    continue;
                                }

                                var extFile = Path.Combine(torrent.SavePath, torrent.Name + ext);
                                if (TorrentPathValidator.IsStrictSubPath(torrent.SavePath, extFile) && File.Exists(extFile))
                                {
                                    await DeletePathWithRetryAsync(extFile, isDirectory: false);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to delete files for torrent {0}", torrent.Name);
                    }
                }
            }

            this.eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = deleteFiles });
        }
        finally
        {
            semaphore.Release();
            if (semaphore.CurrentCount > 0)
            {
                this.deletionLocks.TryRemove(id, out _);
            }
        }
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
        var torrent = this.torrentRepository.Get(id);
        if (torrent != null && torrent.Status == TorrentStatus.Paused)
        {
            var newStatus = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
            var old = torrent.Status;
            torrent.Status = newStatus;
            this.torrentRepository.Update(torrent);

            try
            {
                await this.downloadEngine.ResumeTorrentAsync(id);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error resuming torrent in download engine {0}", id);
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

            try
            {
                await this.downloadEngine.ForceRecheckAsync(id);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error rechecking torrent in download engine {0}", id);
            }

            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = TorrentStatus.Checking });
        }
    }

    public async Task ForceAnnounceAsync(int id)
    {
        try
        {
            await this.downloadEngine.ForceAnnounceAsync(id);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error announcing torrent in download engine {0}", id);
        }
    }

    public async Task MoveQueueAsync(int id, string position)
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
            this.torrentRepository.Update(allTorrents[i]);
        }

        if (this.queueManagerService != null)
        {
            await this.queueManagerService.ProcessQueueAsync();
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
            torrent.Downloaded = task.DownloadedBytes;
            torrent.Uploaded = task.UploadedBytes;
            torrent.DownloadSpeed = task.DownloadSpeed;
            torrent.UploadSpeed = task.UploadSpeed;
            torrent.Seeders = task.ConnectedSeeders;
            torrent.Leechers = task.ConnectedLeechers;
            torrent.Ratio = task.DownloadedBytes > 0 ? (double)task.UploadedBytes / task.DownloadedBytes : 0.0;

            if (oldStatus != torrent.Status)
            {
                this.torrentRepository.Update(torrent);
                this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                {
                    Torrent = torrent,
                    OldStatus = oldStatus,
                    NewStatus = torrent.Status,
                });

                if (torrent.Status == TorrentStatus.Stalled)
                {
                    this.eventAggregator.PublishEvent(new HealthIssueEvent(
                        torrent,
                        "Tracker",
                        !string.IsNullOrWhiteSpace(torrent.ErrorMessage) ? torrent.ErrorMessage : "Torrent stalled due to tracker failure.",
                        isResolved: false));
                }
                else if (oldStatus == TorrentStatus.Stalled)
                {
                    this.eventAggregator.PublishEvent(new HealthIssueEvent(
                        torrent,
                        "Tracker",
                        "Tracker recovered and peers connected.",
                        isResolved: true));
                }
            }

            // Record completion timestamp when torrent reaches Seeding
            if (torrent.Status == TorrentStatus.Seeding && !torrent.DateCompleted.HasValue)
            {
                torrent.DateCompleted = DateTime.UtcNow;
                this.torrentRepository.Update(torrent);
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
}
