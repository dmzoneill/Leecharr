using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Torrents;

public record TorrentUploadFailure(string FileName, string Reason);
public record TorrentUploadResult(List<TorrentResource> Added, List<TorrentUploadFailure> Failed);

public class MoveQueueRequest
{
    public string Position { get; set; }
}

public class AddTorrentJsonRequest
{
    public string MagnetLink { get; set; }
    public string MagnetUrl { get; set; }
    public string DownloadUrl { get; set; }
    public string Title { get; set; }
    public string Category { get; set; }
    public string SavePath { get; set; }
    public bool Paused { get; set; }
    public bool StartPaused { get; set; }
}

[V1ApiController("torrents")]
[Route("api/v1/torrent")]
public class TorrentController : RestControllerWithSignalR<TorrentResource, Torrent>
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly ITrackerEntryRepository _trackerEntryRepository;
    private readonly IGeoIpService _geoIpService;
    private readonly IDownloadEngine _downloadEngine;

    public TorrentController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        IMediaEnrichmentService mediaEnrichmentService,
        ITrackerEntryRepository trackerEntryRepository,
        IBroadcastSignalRMessage signalRBroadcaster,
        IGeoIpService geoIpService = null,
        IDownloadEngine downloadEngine = null)
        : base(signalRBroadcaster)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _mediaEnrichmentService = mediaEnrichmentService;
        _trackerEntryRepository = trackerEntryRepository;
        _geoIpService = geoIpService;
        _downloadEngine = downloadEngine;
    }

    [HttpGet]
    public ActionResult<List<TorrentResource>> GetAll()
    {
        var torrents = _torrentService.GetAll();
        var resources = torrents.Select(t =>
        {
            var meta = _mediaEnrichmentService.GetMetadata(t.Id);
            return TorrentResourceMapper.ToResource(t, meta);
        }).ToList();

        return Ok(resources);
    }

    [HttpGet("{id:int}")]
    public ActionResult<TorrentResource> GetById(int id)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        var meta = _mediaEnrichmentService.GetMetadata(id);
        return Ok(TorrentResourceMapper.ToResource(torrent, meta));
    }

    [HttpGet("{id:int}/files")]
    public ActionResult<List<TorrentFileResource>> GetFiles(int id)
    {
        var files = _torrentFileService.GetFiles(id);
        var resources = files.Select(f => new TorrentFileResource
        {
            Id = f.Id,
            TorrentId = f.TorrentId,
            Path = f.Path,
            Size = f.Size,
            PieceOffset = f.PieceOffset,
            PieceCount = f.PieceCount,
            Priority = f.Priority,
            Progress = f.Progress
        }).ToList();

        return Ok(resources);
    }

    [HttpGet("{id:int}/peers")]
    public ActionResult<List<PeerResource>> GetPeers(int id)
    {
        var task = _torrentService.GetDownloadTask(id);
        if (task == null)
        {
            return Ok(new List<PeerResource>());
        }

        var peers = task.GetPeers();
        var resources = peers.Select((p, idx) =>
        {
            var geo = _geoIpService?.Lookup(p.Ip);
            return new PeerResource
            {
                Id = idx + 1,
                Ip = p.Ip,
                Port = p.Port,
                Client = p.Client,
                UploadSpeed = p.UploadSpeed,
                DownloadSpeed = p.DownloadSpeed,
                Uploaded = p.Uploaded,
                Downloaded = p.Downloaded,
                Progress = p.Progress,
                Flags = p.Flags,
                CountryCode = geo?.CountryCode ?? string.Empty,
                CountryName = geo?.CountryName ?? string.Empty,
                City = geo?.City ?? string.Empty
            };
        }).ToList();

        return Ok(resources);
    }

    [HttpGet("{id:int}/trackers")]
    public ActionResult<List<TrackerResource>> GetTrackers(int id)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        var dbTrackers = _trackerEntryRepository.GetByTorrentId(id).ToList();
        if (dbTrackers.Count == 0 && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            dbTrackers.Add(new TrackerEntry
            {
                Id = 1,
                TorrentId = id,
                Url = torrent.TrackerUrl,
                Status = 1,
                Seeders = torrent.Seeders,
                Leechers = torrent.Leechers,
                LastAnnounce = DateTime.UtcNow
            });
        }

        var resources = dbTrackers.Select(t => new TrackerResource
        {
            Id = t.Id,
            Url = t.Url,
            Status = t.Status == 1 ? "Working" : (t.Status == 2 ? "Error" : "Queued"),
            Seeders = t.Seeders > 0 ? t.Seeders : torrent.Seeders,
            Leechers = t.Leechers > 0 ? t.Leechers : torrent.Leechers,
            Downloaded = t.Downloaded,
            LastAnnounce = t.LastAnnounce ?? DateTime.UtcNow,
            Message = !string.IsNullOrWhiteSpace(t.ErrorMessage) ? t.ErrorMessage : "OK"
        }).ToList();

        return Ok(resources);
    }

    [HttpPost("{id:int}/trackers")]
    public ActionResult<TrackerResource> AddTracker(int id, [FromBody] AddTrackerRequest request)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        var entry = new TrackerEntry
        {
            TorrentId = id,
            Url = request?.Url ?? string.Empty,
            Tier = 0,
            Enabled = true,
            Status = 1,
            Seeders = 0,
            Leechers = 0,
            LastAnnounce = DateTime.UtcNow
        };
        var created = _trackerEntryRepository.Insert(entry);
        return Ok(new TrackerResource
        {
            Id = created.Id,
            Url = created.Url,
            Status = "Working",
            Seeders = 0,
            Leechers = 0,
            LastAnnounce = created.LastAnnounce ?? DateTime.UtcNow,
            Message = "OK"
        });
    }

    [HttpDelete("{id:int}/trackers/{trackerId:int}")]
    public ActionResult DeleteTracker(int id, int trackerId)
    {
        _trackerEntryRepository.Delete(trackerId);
        return Ok();
    }

    [HttpPost("{id:int}/trackers/{trackerId:int}/announce")]
    public async Task<ActionResult> AnnounceTracker(int id, int trackerId)
    {
        await _torrentService.ForceAnnounceAsync(id);
        return Ok();
    }

    [HttpGet("{id:int}/logs")]
    public ActionResult<List<TorrentEventLogResource>> GetLogs(int id, [FromQuery] int count = 100)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        var list = new List<TorrentEventLogResource>
        {
            new()
            {
                Id = 1,
                TorrentId = id,
                Level = "Info",
                Message = $"Torrent {torrent.Name} added to download queue",
                Timestamp = torrent.DateAdded
            },
            new()
            {
                Id = 2,
                TorrentId = id,
                Level = "Info",
                Message = $"Status is {torrent.Status} with progress {torrent.Progress * 100:F1}%",
                Timestamp = DateTime.UtcNow
            }
        };

        return Ok(list);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<TorrentResource>> AddTorrentJson([FromBody] AddTorrentJsonRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request body is empty.");
        }

        var magnet = !string.IsNullOrWhiteSpace(request.MagnetLink) ? request.MagnetLink : request.MagnetUrl;
        var isPaused = request.Paused || request.StartPaused;

        if (!string.IsNullOrWhiteSpace(magnet))
        {
            var torrent = await _torrentService.AddFromMagnetAsync(magnet, request.Category, request.SavePath, isPaused);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var bytes = await httpClient.GetByteArrayAsync(request.DownloadUrl);
            var parsed = _torrentFileParser.Parse(bytes);
            var torrent = await _torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, isPaused, bytes);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        return BadRequest("Either magnetLink or downloadUrl is required.");
    }

    [HttpPost]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    public async Task<ActionResult<TorrentResource>> AddTorrentForm(
        [FromForm] IFormFile file = null,
        [FromForm] string magnetUrl = null,
        [FromForm] string category = null,
        [FromForm] string savePath = null,
        [FromForm] bool paused = false,
        [FromForm] bool startPaused = false)
    {
        var isPaused = paused || startPaused;

        if (file != null && file.Length > 0)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = _torrentFileParser.Parse(bytes);

            var torrent = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(magnetUrl))
        {
            var torrent = await _torrentService.AddFromMagnetAsync(magnetUrl, category, savePath, isPaused);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        return BadRequest("Either a .torrent file or a magnetUrl is required.");
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] List<IFormFile> files = null,
        [FromForm] string category = null,
        [FromForm] bool isPaused = false,
        [FromForm] bool startPaused = false)
    {
        var formFiles = new List<IFormFile>();
        if (files != null && files.Count > 0)
        {
            formFiles.AddRange(files);
        }

        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            foreach (var f in Request.Form.Files)
            {
                if (!formFiles.Contains(f))
                {
                    formFiles.Add(f);
                }
            }
        }

        if (formFiles.Count == 0)
        {
            return BadRequest("No torrent file provided");
        }

        var pausedFlag = isPaused || startPaused;
        var added = new List<TorrentResource>();
        var failed = new List<TorrentUploadFailure>();

        foreach (var file in formFiles)
        {
            if (file == null || file.Length == 0)
            {
                continue;
            }

            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var parsed = _torrentFileParser.Parse(bytes);

                var torrent = await _torrentService.AddFromParsedTorrentAsync(parsed, category, null, pausedFlag, bytes);
                var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
                added.Add(TorrentResourceMapper.ToResource(torrent, meta));
            }
            catch (Exception ex)
            {
                failed.Add(new TorrentUploadFailure(file.FileName, ex.Message));
            }
        }

        return Ok(new TorrentUploadResult(added, failed));
    }

    [HttpPost("grab")]
    [Consumes("application/json")]
    public async Task<ActionResult<TorrentResource>> GrabRelease([FromBody] AddTorrentJsonRequest request)
    {
        return await AddTorrentJson(request);
    }

    [HttpPost("{id:int}/pause")]
    public async Task<ActionResult> Pause(int id)
    {
        await _torrentService.PauseAsync(id);
        return Ok();
    }

    [HttpPost("{id:int}/resume")]
    public async Task<ActionResult> Resume(int id)
    {
        await _torrentService.ResumeAsync(id);
        return Ok();
    }

    [HttpPost("{id:int}/recheck")]
    public async Task<ActionResult> Recheck(int id)
    {
        await _torrentService.ForceRecheckAsync(id);
        return Ok();
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TorrentResource>> Update(int id, [FromBody] TorrentResource resource)
    {
        var existing = _torrentService.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(resource.Category))
        {
            existing.Category = resource.Category;
        }

        if (!string.IsNullOrWhiteSpace(resource.Label))
        {
            existing.Label = resource.Label;
        }

        if (resource.Priority.HasValue)
        {
            existing.Priority = resource.Priority.Value;
        }

        if (resource.UploadLimit.HasValue)
        {
            existing.UploadLimit = resource.UploadLimit.Value;
        }

        if (resource.DownloadLimit.HasValue)
        {
            existing.DownloadLimit = resource.DownloadLimit.Value;
        }

        if (resource.SequentialDownload.HasValue)
        {
            existing.SequentialDownload = resource.SequentialDownload.Value;
        }

        if (resource.InitialSeeding.HasValue)
        {
            existing.InitialSeeding = resource.InitialSeeding.Value;
        }

        if (!string.IsNullOrWhiteSpace(resource.Name))
        {
            existing.Name = resource.Name;
        }

        var updated = await _torrentService.UpdateAsync(existing);
        if (_downloadEngine != null && (resource.UploadLimit.HasValue || resource.DownloadLimit.HasValue))
        {
            await _downloadEngine.SetTorrentRateLimitsAsync(updated.Id, updated.DownloadLimit, updated.UploadLimit);
        }

        var meta = _mediaEnrichmentService.GetMetadata(id);
        return Ok(TorrentResourceMapper.ToResource(updated, meta));
    }

    [HttpPost("{id:int}/announce")]
    public async Task<ActionResult> Announce(int id)
    {
        await _torrentService.ForceAnnounceAsync(id);
        return Ok();
    }

    [HttpPost("{id:int}/queue")]
    [HttpPut("{id:int}/queue")]
    public async Task<ActionResult> MoveQueue(int id, [FromBody] MoveQueueRequest request)
    {
        await _torrentService.MoveQueueAsync(id, request?.Position);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, [FromQuery] bool deleteFiles = false)
    {
        await _torrentService.DeleteAsync(id, deleteFiles);
        return NoContent();
    }

    protected override TorrentResource GetResourceById(Torrent model)
    {
        if (model == null)
        {
            return null;
        }

        var meta = _mediaEnrichmentService.GetMetadata(model.Id);
        return TorrentResourceMapper.ToResource(model, meta);
    }
}
