using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using Leecharr.Http.REST;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Torrents;

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
