// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Leecharr.Api.V1.Media;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Torrents;

[V1ApiController("downloadhistory")]
public class DownloadHistoryController : Controller
{
    private readonly IDownloadHistoryService historyService;
    private readonly ITorrentMediaMetadataRepository mediaMetadataRepository;
    private readonly IMediaEnrichmentService mediaEnrichmentService;

    public DownloadHistoryController(
        IDownloadHistoryService historyService,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null,
        IMediaEnrichmentService mediaEnrichmentService = null)
    {
        this.historyService = historyService;
        this.mediaMetadataRepository = mediaMetadataRepository;
        this.mediaEnrichmentService = mediaEnrichmentService;
    }

    [HttpGet]
    public ActionResult<List<DownloadHistoryResource>> GetAll(
        [FromQuery] string query = null,
        [FromQuery] string status = null,
        [FromQuery] int limit = 500)
    {
        var records = this.historyService.GetAll(query, status, limit);
        return this.Ok(records.Select(this.ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<DownloadHistoryResource> Get(int id)
    {
        var record = this.historyService.Get(id);
        if (record == null)
        {
            return this.NotFound();
        }

        return this.Ok(this.ToResource(record));
    }

    [HttpPost("{id:int}/readd")]
    public async Task<ActionResult<TorrentResource>> ReAdd(int id)
    {
        try
        {
            var added = await this.historyService.ReAddAsync(id);
            return this.Ok(TorrentResourceMapper.ToResource(added));
        }
        catch (ArgumentException ex)
        {
            return this.NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return this.Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return this.BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/enrich")]
    public async Task<ActionResult<DownloadHistoryResource>> Enrich(int id)
    {
        var record = this.historyService.Get(id);
        if (record == null)
        {
            return this.NotFound();
        }

        if (this.mediaEnrichmentService != null)
        {
            var torrent = new Torrent
            {
                Id = record.TorrentId ?? record.Id,
                Name = record.Title,
                InfoHash = record.InfoHash ?? string.Empty,
            };
            await this.mediaEnrichmentService.EnrichTorrentAsync(torrent);
        }

        var resource = this.ToResource(record);
        return this.Ok(resource);
    }

    [HttpPost("enrich-all")]
    public async Task<ActionResult> EnrichAll()
    {
        var records = this.historyService.GetAll(null, null, 1000);
        if (this.mediaEnrichmentService != null)
        {
            foreach (var record in records)
            {
                try
                {
                    var torrent = new Torrent
                    {
                        Id = record.TorrentId ?? record.Id,
                        Name = record.Title,
                        InfoHash = record.InfoHash ?? string.Empty,
                    };
                    await this.mediaEnrichmentService.EnrichTorrentAsync(torrent);
                }
                catch
                {
                }
            }
        }

        return this.Ok(new { message = "Enrichment completed", processedCount = records.Count });
    }

    [HttpPost("reconcile")]
    public ActionResult Reconcile()
    {
        var count = this.historyService.ReconcileAllTorrents();
        return this.Ok(new { success = true, processedCount = count });
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.historyService.Delete(id);
        return this.Ok();
    }

    [HttpDelete]
    public ActionResult ClearAll()
    {
        this.historyService.ClearAll();
        return this.Ok();
    }

    private DownloadHistoryResource ToResource(DownloadHistory model)
    {
        TorrentMediaMetadata metadata = null;
        if (model.TorrentId.HasValue && this.mediaMetadataRepository != null)
        {
            metadata = this.mediaMetadataRepository.GetByTorrentId(model.TorrentId.Value);
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
            Metadata = MediaMetadataResourceMapper.ToResource(metadata),
        };
    }
}
