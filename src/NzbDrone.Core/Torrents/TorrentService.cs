// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Torrents;

public class TorrentService : ITorrentService
{
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentFileRepository fileRepository;
    private readonly ICategoryService categoryService;
    private readonly IMediaEnrichmentService mediaEnrichmentService;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    public TorrentService(
        ITorrentRepository torrentRepository,
        ITorrentFileRepository fileRepository,
        ICategoryService categoryService,
        IMediaEnrichmentService mediaEnrichmentService,
        IConfigService configService,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator,
        ITrackerEntryRepository trackerEntryRepository = null)
    {
        this.torrentRepository = torrentRepository;
        this.fileRepository = fileRepository;
        this.categoryService = categoryService;
        this.mediaEnrichmentService = mediaEnrichmentService;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
        this.trackerEntryRepository = trackerEntryRepository;
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
            : this.categoryService.GetSavePathForCategory(effectiveCategory);

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

        var inserted = this.torrentRepository.Insert(torrent);

        // Insert torrent files
        if (parsed.Files != null)
        {
            var pieceLength = Math.Max(1, parsed.PieceLength);
            long currentByteOffset = 0;
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
                this.fileRepository.Insert(torrentFile);
                currentByteOffset += file.Size;
            }
        }

        // Insert trackers
        if (this.trackerEntryRepository != null)
        {
            if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
            {
                for (var tier = 0; tier < parsed.AnnounceList.Count; tier++)
                {
                    foreach (var url in parsed.AnnounceList[tier])
                    {
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            this.trackerEntryRepository.Insert(new TrackerEntry
                            {
                                TorrentId = inserted.Id,
                                Url = url,
                                Tier = tier,
                                Enabled = true,
                                Status = 1,
                                AnnounceInterval = 1800,
                                LastAnnounce = inserted.DateAdded,
                                NextAnnounce = inserted.DateAdded.AddSeconds(1800),
                                TotalAnnounces = 1,
                                SuccessfulAnnounces = 1,
                            });
                        }
                    }
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
                    Status = 1,
                    AnnounceInterval = 1800,
                    LastAnnounce = inserted.DateAdded,
                    NextAnnounce = inserted.DateAdded.AddSeconds(1800),
                    TotalAnnounces = 1,
                    SuccessfulAnnounces = 1,
                });
            }
        }

        this.logger.Info("Added torrent: {0} ({1})", inserted.Name, inserted.InfoHash);

        if (rawBytes != null && rawBytes.Length > 0 && !string.IsNullOrWhiteSpace(inserted.InfoHash))
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
            : this.categoryService.GetSavePathForCategory(effectiveCategory);

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

        var inserted = this.torrentRepository.Insert(torrent);

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
                        Status = 1,
                        AnnounceInterval = 1800,
                        LastAnnounce = inserted.DateAdded,
                        NextAnnounce = inserted.DateAdded.AddSeconds(1800),
                        TotalAnnounces = 1,
                        SuccessfulAnnounces = 1,
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
        this.torrentRepository.Delete(id);

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var file1 = Path.Combine(appData, "Torrents", $"{torrent.InfoHash.ToLowerInvariant()}.torrent");
            if (File.Exists(file1))
            {
                File.Delete(file1);
            }

            var file2 = Path.Combine(appData, "Leecharr", "Torrents", $"{torrent.InfoHash.ToLowerInvariant()}.torrent");
            if (File.Exists(file2))
            {
                File.Delete(file2);
            }
        }
        catch
        {
        }

        if (deleteFiles && !string.IsNullOrWhiteSpace(torrent.SavePath) && Directory.Exists(torrent.SavePath))
        {
            try
            {
                var torrentFolder = Path.Combine(torrent.SavePath, torrent.Name);
                if (Directory.Exists(torrentFolder))
                {
                    Directory.Delete(torrentFolder, true);
                }
                else if (File.Exists(torrentFolder))
                {
                    File.Delete(torrentFolder);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to delete files for torrent {0}", torrent.Name);
            }
        }

        this.eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = deleteFiles });
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

        await Task.CompletedTask;
    }

    private void SyncWithEngine(Torrent torrent)
    {
        var task = this.downloadEngine.GetTask(torrent.Id);
        if (task != null)
        {
            torrent.Status = task.Status;
            torrent.Progress = task.Progress;
            torrent.Downloaded = task.DownloadedBytes;
            torrent.Uploaded = task.UploadedBytes;
            torrent.DownloadSpeed = task.DownloadSpeed;
            torrent.UploadSpeed = task.UploadSpeed;
            torrent.Seeders = task.ConnectedSeeders;
            torrent.Leechers = task.ConnectedLeechers;
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
}
