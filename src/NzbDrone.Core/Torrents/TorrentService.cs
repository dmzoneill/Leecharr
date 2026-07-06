using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public interface ITorrentService
{
    IEnumerable<Torrent> GetAll();
    Torrent Get(int id);
    Torrent GetByInfoHash(string infoHash);
    Task<Torrent> AddFromParsedTorrentAsync(ParsedTorrent parsed, string category = null, string savePath = null, bool startPaused = false, byte[] rawBytes = null);
    Task<Torrent> AddFromMagnetAsync(string magnetUri, string category = null, string savePath = null, bool startPaused = false);
    Task<Torrent> UpdateAsync(Torrent torrent);
    Task DeleteAsync(int id, bool deleteFiles = false);
    Task PauseAsync(int id);
    Task ResumeAsync(int id);
    Task ForceRecheckAsync(int id);
}

public class TorrentService : ITorrentService
{
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentFileRepository _fileRepository;
    private readonly ICategoryService _categoryService;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public TorrentService(
        ITorrentRepository torrentRepository,
        ITorrentFileRepository fileRepository,
        ICategoryService categoryService,
        IMediaEnrichmentService mediaEnrichmentService,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _torrentRepository = torrentRepository;
        _fileRepository = fileRepository;
        _categoryService = categoryService;
        _mediaEnrichmentService = mediaEnrichmentService;
        _configService = configService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<Torrent> GetAll()
    {
        return _torrentRepository.All().OrderByDescending(t => t.DateAdded);
    }

    public Torrent Get(int id)
    {
        return _torrentRepository.Get(id);
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        return _torrentRepository.GetByInfoHash(infoHash.ToLowerInvariant());
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
            Status = startPaused ? TorrentStatus.Paused : TorrentStatus.Queued,
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
            Status = startPaused ? TorrentStatus.Paused : TorrentStatus.Queued,
            Category = effectiveCategory,
            SavePath = effectiveSavePath,
            DateAdded = DateTime.UtcNow,
            TagIds = new List<int>()
        };

        var inserted = _torrentRepository.Insert(torrent);
        _logger.Info("Added magnet torrent: {0} ({1})", inserted.Name, inserted.InfoHash);
        _eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = inserted });

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
        return updated;
    }

    public async Task DeleteAsync(int id, bool deleteFiles = false)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent == null)
        {
            return;
        }

        _logger.Info("Deleting torrent {0} (DeleteFiles={1})", torrent.Name, deleteFiles);

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
            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = TorrentStatus.Paused });
        }

        await Task.CompletedTask;
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
            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = newStatus });
        }

        await Task.CompletedTask;
    }

    public async Task ForceRecheckAsync(int id)
    {
        var torrent = _torrentRepository.Get(id);
        if (torrent != null)
        {
            var old = torrent.Status;
            torrent.Status = TorrentStatus.Checking;
            _torrentRepository.Update(torrent);
            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = torrent, OldStatus = old, NewStatus = TorrentStatus.Checking });
        }

        await Task.CompletedTask;
    }
}
