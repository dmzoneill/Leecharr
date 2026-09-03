// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Api.V1.Torrents;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Indexers;

[V1ApiController("indexers")]
[Route("api/v1/indexer")]
public class IndexerController : Controller
{
    private readonly IIndexerRepository indexerRepository;
    private readonly ITorznabClient torznabClient;
    private readonly IProwlarrSyncService prowlarrSyncService;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public IndexerController(
        IIndexerRepository indexerRepository,
        ITorznabClient torznabClient,
        IProwlarrSyncService prowlarrSyncService,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.indexerRepository = indexerRepository;
        this.torznabClient = torznabClient;
        this.prowlarrSyncService = prowlarrSyncService;
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    [HttpGet]
    public ActionResult<List<IndexerResource>> GetAll()
    {
        var definitions = this.indexerRepository.All();
        return this.Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<IndexerResource> Get(int id)
    {
        var definition = this.indexerRepository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<IndexerResource> Create([FromBody] IndexerResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var created = this.indexerRepository.Insert(model);
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<IndexerResource> Update(int id, [FromBody] IndexerResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var existing = this.indexerRepository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        this.indexerRepository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.indexerRepository.Delete(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<IndexerTestResult>> Test(int id)
    {
        var indexer = this.indexerRepository.Get(id);
        if (indexer == null)
        {
            return this.NotFound();
        }

        return await this.TestDirectInternal(indexer);
    }

    [HttpPost("test")]
    public async Task<ActionResult<IndexerTestResult>> TestDirect([FromBody] IndexerResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        return await this.TestDirectInternal(model);
    }

    [HttpPost("sync-prowlarr")]
    public async Task<ActionResult<object>> SyncProwlarr([FromQuery] string url = "http://localhost:9696", [FromQuery] string apiKey = "")
    {
        try
        {
            var count = await this.prowlarrSyncService.SyncFromProwlarrAsync(url, apiKey);
            return this.Ok(new { success = true, syncedCount = count });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to sync with Prowlarr");
            return this.BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("search")]
    [HttpPost("search")]
    public async Task<ActionResult<List<ReleaseInfoResource>>> Search(
        [FromQuery] string query = null,
        [FromQuery] string category = null,
        [FromQuery] int? indexerId = null,
        [FromQuery] bool freeleechOnly = false,
        [FromQuery] int? season = null,
        [FromQuery] int? ep = null,
        [FromQuery] string imdbId = null,
        [FromQuery] string tmdbId = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string type = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] IndexerSearchRequest request = null)
    {
        var effectiveQuery = !string.IsNullOrEmpty(request?.Query) ? request.Query : query;
        var effectiveCategory = !string.IsNullOrEmpty(request?.Category) ? request.Category : category;
        var effectiveIndexerId = request?.IndexerId ?? indexerId;
        var effectiveFreeleech = request?.FreeleechOnly ?? freeleechOnly;
        var effectiveSeason = request?.Season ?? season;
        var effectiveEp = request?.Ep ?? ep;
        var effectiveImdb = !string.IsNullOrEmpty(request?.ImdbId) ? request.ImdbId : imdbId;
        var effectiveTmdb = !string.IsNullOrEmpty(request?.TmdbId) ? request.TmdbId : tmdbId;
        var effectiveOffset = request != null && request.Offset > 0 ? request.Offset : offset;
        var effectiveLimit = request != null && request.Limit > 0 ? request.Limit : limit;
        var effectiveType = !string.IsNullOrEmpty(request?.Type) ? request.Type : type;

        var indexers = effectiveIndexerId.HasValue
            ? new List<IndexerDefinition> { this.indexerRepository.Get(effectiveIndexerId.Value) }.Where(i => i != null).ToList()
            : this.indexerRepository.GetSearchEnabled().ToList();

        var catId = int.TryParse(effectiveCategory, out var parsedCat) && parsedCat > 0 ? (int?)parsedCat : null;

        var allResults = new List<ReleaseInfoResource>();
        var searchTasks = indexers.Select(async idx =>
        {
            try
            {
                var results = await this.torznabClient.SearchAsync(idx, effectiveQuery ?? string.Empty, catId, effectiveLimit, effectiveOffset, effectiveSeason, effectiveEp, effectiveImdb, effectiveTmdb, effectiveType);
                return results.Select(r => new ReleaseInfoResource
                {
                    Title = r.Title,
                    Guid = r.Guid,
                    Link = r.DownloadUrl ?? r.MagnetUrl,
                    Comments = string.Empty,
                    PublishDate = r.PublishDate,
                    Category = r.Category,
                    Size = r.Size,
                    DownloadUrl = r.DownloadUrl,
                    MagnetUrl = r.MagnetUrl,
                    InfoHash = r.InfoHash,
                    Seeders = r.Seeders,
                    Leechers = r.Leechers,
                    IndexerId = idx.Id,
                    IndexerName = idx.Name,
                    DownloadVolumeFactor = r.DownloadVolumeFactor,
                    UploadVolumeFactor = r.UploadVolumeFactor,
                }).ToList();
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to search indexer {0}", idx.Name);
                return new List<ReleaseInfoResource>();
            }
        });

        var resultsArray = await Task.WhenAll(searchTasks);
        foreach (var rList in resultsArray)
        {
            allResults.AddRange(rList);
        }

        if (freeleechOnly)
        {
            allResults = allResults.Where(r => r.IsFreeleech).ToList();
        }

        return this.Ok(allResults.OrderByDescending(r => r.Seeders).ToList());
    }

    [HttpPost("download")]
    public async Task<ActionResult<TorrentResource>> DownloadRelease([FromBody] DownloadReleaseRequest request)
    {
        if (request == null)
        {
            return this.BadRequest("Request is null.");
        }

        Torrent torrent = null;
        if (!string.IsNullOrWhiteSpace(request.MagnetUrl))
        {
            torrent = await this.torrentService.AddFromMagnetAsync(request.MagnetUrl, request.Category, request.SavePath, request.StartPaused);
        }
        else if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            if (request.DownloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                torrent = await this.torrentService.AddFromMagnetAsync(request.DownloadUrl, request.Category, request.SavePath, request.StartPaused);
            }
            else
            {
                var bytes = await this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl);
                var parsed = this.torrentFileParser.Parse(bytes);
                torrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, request.StartPaused, bytes);
            }
        }

        if (torrent == null)
        {
            return this.BadRequest("Failed to grab release.");
        }

        return this.Ok(TorrentResourceMapper.ToResource(torrent));
    }

    private async Task<ActionResult<IndexerTestResult>> TestDirectInternal(IndexerDefinition indexer)
    {
        try
        {
            var results = await this.torznabClient.SearchAsync(indexer, string.Empty, limit: 1);
            return this.Ok(new IndexerTestResult
            {
                Success = true,
                Message = $"Connected successfully to {indexer.Name}.",
            });
        }
        catch (Exception ex)
        {
            return this.Ok(new IndexerTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}",
            });
        }
    }

    private static IndexerResource ToResource(IndexerDefinition model)
    {
        return new IndexerResource
        {
            Id = model.Id,
            Name = model.Name,
            Implementation = model.Implementation,
            ConfigContract = model.ConfigContract,
            Settings = model.Settings,
            Enable = model.Enable,
            Priority = model.Priority,
            Url = model.Url,
            ApiKey = model.ApiKey,
            Categories = model.Categories ?? new List<int>(),
            EnableRss = model.EnableRss,
            EnableSearch = model.EnableSearch,
            FreeleechOnly = model.FreeleechOnly,
            MinSeeders = model.MinSeeders,
            DownloadClientId = model.DownloadClientId,
            Tags = model.Tags ?? new List<int>(),
        };
    }

    private static IndexerDefinition ToModel(IndexerResource resource)
    {
        return new IndexerDefinition
        {
            Id = resource.Id,
            Name = resource.Name,
            Implementation = resource.Implementation ?? "Torznab",
            ConfigContract = resource.ConfigContract,
            Settings = resource.Settings,
            Enable = resource.Enable,
            Priority = resource.Priority,
            Url = resource.Url,
            ApiKey = resource.ApiKey ?? string.Empty,
            Categories = resource.Categories ?? new List<int>(),
            EnableRss = resource.EnableRss,
            EnableSearch = resource.EnableSearch,
            FreeleechOnly = resource.FreeleechOnly,
            MinSeeders = resource.MinSeeders,
            DownloadClientId = resource.DownloadClientId,
            Tags = resource.Tags ?? new List<int>(),
        };
    }
}
