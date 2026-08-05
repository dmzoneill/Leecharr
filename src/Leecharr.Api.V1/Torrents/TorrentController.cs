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

public class AddTorrentJsonRequest
{
    public string MagnetLink { get; set; }
    public string DownloadUrl { get; set; }
    public string Title { get; set; }
    public string Category { get; set; }
    public string SavePath { get; set; }
    public bool Paused { get; set; }
}

[V1ApiController("torrents")]
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
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    public async Task<ActionResult<TorrentResource>> AddTorrent(
        [FromForm] IFormFile file = null,
        [FromForm] string magnetUrl = null,
        [FromForm] string category = null,
        [FromForm] string savePath = null,
        [FromForm] bool paused = false)
    {
        if (file != null && file.Length > 0)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = _torrentFileParser.Parse(bytes);

            var torrent = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, paused, bytes);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(magnetUrl))
        {
            var torrent = await _torrentService.AddFromMagnetAsync(magnetUrl, category, savePath, paused);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        return BadRequest("Either a .torrent file or a magnetUrl is required.");
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm(Name = "file")] List<IFormFile> files, [FromForm] string category = null, [FromForm] bool isPaused = false)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest("No torrent file provided");
        }

        var added = new List<TorrentResource>();
        var failed = new List<TorrentUploadFailure>();

        foreach (var file in files)
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

                var torrent = await _torrentService.AddFromParsedTorrentAsync(parsed, category, null, isPaused, bytes);
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
        if (request == null)
        {
            return BadRequest("Request body is empty.");
        }

        if (!string.IsNullOrWhiteSpace(request.MagnetLink))
        {
            var torrent = await _torrentService.AddFromMagnetAsync(request.MagnetLink, request.Category, request.SavePath, request.Paused);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var bytes = await httpClient.GetByteArrayAsync(request.DownloadUrl);
            var parsed = _torrentFileParser.Parse(bytes);
            var torrent = await _torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, request.Paused, bytes);
            var meta = _mediaEnrichmentService.GetMetadata(torrent.Id);
            return Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        return BadRequest("Either magnetLink or downloadUrl is required.");
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
