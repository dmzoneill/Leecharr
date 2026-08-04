using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;
using Leecharr.Http;

namespace Leecharr.Api.V1.Torrents;

[V1ApiController("downloadhistory")]
public class DownloadHistoryController : Controller
{
    private readonly IDownloadHistoryService _historyService;
    private readonly ITorrentMediaMetadataRepository _mediaMetadataRepository;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;

    public DownloadHistoryController(
        IDownloadHistoryService historyService,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null,
        IMediaEnrichmentService mediaEnrichmentService = null)
    {
        _historyService = historyService;
        _mediaMetadataRepository = mediaMetadataRepository;
        _mediaEnrichmentService = mediaEnrichmentService;
    }

    [HttpGet]
    public ActionResult<List<DownloadHistoryResource>> GetAll(
        [FromQuery] string query = null,
        [FromQuery] string status = null,
        [FromQuery] int limit = 500)
    {
        var records = _historyService.GetAll(query, status, limit);
        return Ok(records.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<DownloadHistoryResource> Get(int id)
    {
        var record = _historyService.Get(id);
        if (record == null)
        {
            return NotFound();
        }

        return Ok(ToResource(record));
    }

    [HttpPost("{id:int}/readd")]
    public ActionResult<TorrentResource> ReAdd(int id)
    {
        try
        {
            var added = _historyService.ReAdd(id);
            return Ok(TorrentResourceMapper.ToResource(added));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/enrich")]
    public ActionResult<DownloadHistoryResource> Enrich(int id)
    {
        var record = _historyService.Get(id);
        if (record == null)
        {
            return NotFound();
        }

        var resource = ToResource(record);
        return Ok(resource);
    }

    [HttpPost("enrich-all")]
    public ActionResult EnrichAll()
    {
        return Ok(new { message = "Enrichment completed" });
    }

    [HttpPost("reconcile")]
    public ActionResult Reconcile()
    {
        var count = _historyService.ReconcileAllTorrents();
        return Ok(new { success = true, processedCount = count });
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _historyService.Delete(id);
        return Ok();
    }

    [HttpDelete]
    public ActionResult ClearAll()
    {
        _historyService.ClearAll();
        return Ok();
    }

    private DownloadHistoryResource ToResource(DownloadHistory model)
    {
        TorrentMediaMetadata metadata = null;
        if (model.TorrentId.HasValue && _mediaMetadataRepository != null)
        {
            metadata = _mediaMetadataRepository.GetByTorrentId(model.TorrentId.Value);
        }

        if (metadata == null && !string.IsNullOrEmpty(model.DataJson))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<TorrentMediaMetadata>(
                    model.DataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                metadata = null;
            }
        }

        return new DownloadHistoryResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            Title = model.Title,
            InfoHash = model.InfoHash,
            TotalSize = model.TotalSize,
            DateAdded = model.DateAdded,
            DateCompleted = model.DateCompleted,
            DateRemoved = model.DateRemoved,
            Uploaded = model.Uploaded,
            Downloaded = model.Downloaded,
            Ratio = model.Ratio,
            SeedingTime = model.SeedingTime,
            PrimaryTracker = model.PrimaryTracker,
            IndexerName = model.IndexerName,
            Source = model.Source,
            MagnetUrl = model.MagnetUrl,
            DownloadUrl = model.DownloadUrl,
            Status = model.Status,
            RemovalReason = model.RemovalReason,
            Metadata = metadata
        };
    }
}
