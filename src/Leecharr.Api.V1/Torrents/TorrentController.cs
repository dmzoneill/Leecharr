using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using Leecharr.Http.REST;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;
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

    public TorrentController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        IMediaEnrichmentService mediaEnrichmentService,
        IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _mediaEnrichmentService = mediaEnrichmentService;
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
