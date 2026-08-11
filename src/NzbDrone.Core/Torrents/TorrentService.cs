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

namespace NzbDrone.Core.Torrents;

public class TorrentService : ITorrentService
{
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentFileRepository _fileRepository;
    private readonly ICategoryService _categoryService;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly IConfigService _configService;
    private readonly IDownloadEngine _downloadEngine;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public TorrentService(
        ITorrentRepository torrentRepository,
        ITorrentFileRepository fileRepository,
        ICategoryService categoryService,
        IMediaEnrichmentService mediaEnrichmentService,
        IConfigService configService,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator)
    {
        _torrentRepository = torrentRepository;
        _fileRepository = fileRepository;
        _categoryService = categoryService;
        _mediaEnrichmentService = mediaEnrichmentService;
        _configService = configService;
        _downloadEngine = downloadEngine;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<Torrent> GetAll()
    {
        var torrents = _torrentRepository.All().OrderByDescending(t => t.DateAdded).ToList();
        foreach (var torrent in torrents)
        {
            SyncWithEngine(torrent);
        }

        return torrents;
    }

    public Torrent Get(int id)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent != null)
        {
            SyncWithEngine(torrent);
        }

        return torrent;
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var torrent = _torrentRepository.GetByInfoHash(infoHash.ToLowerInvariant());
        if (torrent != null)
        {
            SyncWithEngine(torrent);
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

        var existing = _torrentRepository.GetByInfoHash(parsed.InfoHash);
        if (existing != null)
        {
            _logger.Warn("Torrent with infohash {0} already exists", parsed.InfoHash);
            return existing;
        }

        var effectiveCategory = !string.IsNullOrWhiteSpace(category) ? category : _configService.DefaultCategory;
        var effectiveSavePath = !string.IsNullOrWhiteSpace(savePath)
            ? savePath
            : _categoryService.GetSavePathForCategory(effectiveCategory);

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
            Status = startPaused ? TorrentStatus.Paused : TorrentStatus.Downloading,
            Category = effectiveCategory,
            SavePath = effectiveSavePath,
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>()
        };

        var inserted = _torrentRepository.Insert(torrent);

        // Insert torrent files
        var pieceOffset = 0;
        if (parsed.Files != null)
        {
            foreach (var file in parsed.Files)
            {
                var pieceCount = (int)Math.Ceiling((double)file.Size / Math.Max(1, parsed.PieceLength));
                var torrentFile = new TorrentFile
                {
                    TorrentId = inserted.Id,
                    Path = file.Path,
                    Size = file.Size,
                    PieceOffset = pieceOffset,
                    PieceCount = pieceCount,
                    Priority = 1,
                    Progress = 0.0
                };
                _fileRepository.Insert(torrentFile);
                pieceOffset += pieceCount;
            }
        }

        _logger.Info("Added torrent: {0} ({1})", inserted.Name, inserted.InfoHash);

        // Start torrent in BitTorrent download engine
        try
        {
            await _downloadEngine.AddTorrentAsync(inserted, rawBytes, null);
            if (startPaused)
            {
                await _downloadEngine.PauseTorrentAsync(inserted.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start download engine task for torrent {0}", inserted.Name);
        }

        _eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = inserted });

        // Trigger asynchronous media enrichment
        if (_configService.AutoEnrichEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _mediaEnrichmentService.EnrichTorrentAsync(inserted);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Background media enrichment failed for torrent {0}", inserted.Id);
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
        var existing = _torrentRepository.GetByInfoHash(parsedMagnet.InfoHash);
        if (existing != null)
        {
            _logger.Warn("Torrent with infohash {0} already exists", parsedMagnet.InfoHash);
            return existing;
        }

        var effectiveCategory = !string.IsNullOrWhiteSpace(category) ? category : _configService.DefaultCategory;
        var effectiveSavePath = !string.IsNullOrWhiteSpace(savePath)
            ? savePath
            : _categoryService.GetSavePathForCategory(effectiveCategory);

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
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>()
        };

        var inserted = _torrentRepository.Insert(torrent);
        _logger.Info("Added magnet torrent: {0} ({1})", inserted.Name, inserted.InfoHash);

        // Start torrent in BitTorrent download engine
        try
        {
            await _downloadEngine.AddTorrentAsync(inserted, null, magnetUri);
            if (startPaused)
            {
                await _downloadEngine.PauseTorrentAsync(inserted.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start download engine task for magnet {0}", inserted.Name);
        }

        _eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = inserted });

        // Trigger asynchronous media enrichment
        if (_configService.AutoEnrichEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _mediaEnrichmentService.EnrichTorrentAsync(inserted);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Background media enrichment failed for magnet {0}", inserted.Id);
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

        var updated = _torrentRepository.Update(torrent);
        _eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = updated });
        return await Task.FromResult(updated);
    }

    public async Task DeleteAsync(int id, bool deleteFiles = false)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent == null)
        {
            return;
        }

        _logger.Info("Deleting torrent {0} (DeleteFiles={1})", torrent.Name, deleteFiles);

        try
        {
            await _downloadEngine.RemoveTorrentAsync(id, deleteFiles);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error removing torrent {0} from download engine", id);
        }

        _fileRepository.DeleteByTorrentId(id);
        _mediaEnrichmentService.DeleteMetadata(id);
        _torrentRepository.Delete(id);

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
                _logger.Warn(ex, "Failed to delete files for torrent {0}", torrent.Name);
            }
        }

        _eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = deleteFiles });
    }

    public async Task PauseAsync(int id)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent != null && torrent.Status != TorrentStatus.Paused)
        {
            var old = torrent.Status;
            torrent.Status = TorrentStatus.Paused;
            _torrentRepository.Update(torrent);

            try
            {
                await _downloadEngine.PauseTorrentAsync(id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error pausing torrent in download engine {0}", id);
            }

            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = TorrentStatus.Paused });
        }
    }

    public async Task ResumeAsync(int id)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent != null && torrent.Status == TorrentStatus.Paused)
        {
            var newStatus = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
            var old = torrent.Status;
            torrent.Status = newStatus;
            _torrentRepository.Update(torrent);

            try
            {
                await _downloadEngine.ResumeTorrentAsync(id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error resuming torrent in download engine {0}", id);
            }

            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = newStatus });
        }
    }

    public async Task ForceRecheckAsync(int id)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent != null)
        {
            var old = torrent.Status;
            torrent.Status = TorrentStatus.Checking;
            _torrentRepository.Update(torrent);

            try
            {
                await _downloadEngine.ForceRecheckAsync(id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error rechecking torrent in download engine {0}", id);
            }

            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = TorrentStatus.Checking });
        }
    }

    public async Task ForceAnnounceAsync(int id)
    {
        try
        {
            await _downloadEngine.ForceAnnounceAsync(id);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error announcing torrent in download engine {0}", id);
        }
    }

    public async Task MoveQueueAsync(int id, string position)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent == null)
        {
            return;
        }

        var allTorrents = _torrentRepository.All().OrderBy(t => t.QueuePosition).ToList();
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
            _torrentRepository.Update(allTorrents[i]);
        }

        await Task.CompletedTask;
    }

    private void SyncWithEngine(Torrent torrent)
    {
        var task = _downloadEngine.GetTask(torrent.Id);
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
    }

    public IDownloadTask GetDownloadTask(int torrentId)
    {
        return _downloadEngine.GetTask(torrentId);
    }
}
