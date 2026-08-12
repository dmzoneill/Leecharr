using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NLog;
using Leecharr.Api.V1.Torrents;
using Leecharr.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Indexers;

[V1ApiController("indexers")]
public class IndexerController : Controller
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly IIndexerRepository _indexerRepository;
    private readonly ITorznabClient _torznabClient;
    private readonly IProwlarrSyncService _prowlarrSyncService;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public IndexerController(
        IIndexerRepository indexerRepository,
        ITorznabClient torznabClient,
        IProwlarrSyncService prowlarrSyncService,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser)
    {
        _indexerRepository = indexerRepository;
        _torznabClient = torznabClient;
        _prowlarrSyncService = prowlarrSyncService;
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
    }

    [HttpGet]
    public ActionResult<List<IndexerResource>> GetAll()
    {
        var definitions = _indexerRepository.All();
        return Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<IndexerResource> Get(int id)
    {
        var definition = _indexerRepository.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<IndexerResource> Create([FromBody] IndexerResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        var created = _indexerRepository.Insert(model);
        return Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<IndexerResource> Update(int id, [FromBody] IndexerResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var existing = _indexerRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        _indexerRepository.Update(model);
        return Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _indexerRepository.Delete(id);
        return Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<IndexerTestResult>> Test(int id)
    {
        var indexer = _indexerRepository.Get(id);
        if (indexer == null)
        {
            return NotFound();
        }

        return await TestDirectInternal(indexer);
    }

    [HttpPost("test")]
    public async Task<ActionResult<IndexerTestResult>> TestDirect([FromBody] IndexerResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        return await TestDirectInternal(model);
    }

    [HttpPost("sync-prowlarr")]
    public async Task<ActionResult<object>> SyncProwlarr([FromQuery] string url = "http://localhost:9696", [FromQuery] string apiKey = "")
    {
        try
        {
            var count = await _prowlarrSyncService.SyncFromProwlarrAsync(url, apiKey);
            return Ok(new { success = true, syncedCount = count });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to sync with Prowlarr");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<ReleaseInfoResource>>> Search(
        [FromQuery] string query = null,
        [FromQuery] string category = null,
        [FromQuery] int? indexerId = null,
        [FromQuery] bool freeleechOnly = false)
    {
        var indexers = indexerId.HasValue
            ? new List<IndexerDefinition> { _indexerRepository.Get(indexerId.Value) }.Where(i => i != null).ToList()
            : _indexerRepository.GetSearchEnabled().ToList();

        var allResults = new List<ReleaseInfoResource>();
        var searchTasks = indexers.Select(async idx =>
        {
            try
            {
                var results = await _torznabClient.SearchAsync(idx, query ?? string.Empty);
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
                    UploadVolumeFactor = r.UploadVolumeFactor
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to search indexer {0}", idx.Name);
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

        return Ok(allResults.OrderByDescending(r => r.Seeders).ToList());
    }

    [HttpPost("download")]
    public async Task<ActionResult<TorrentResource>> DownloadRelease([FromBody] DownloadReleaseRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request is null.");
        }

        Torrent torrent = null;
        if (!string.IsNullOrWhiteSpace(request.MagnetUrl))
        {
            torrent = await _torrentService.AddFromMagnetAsync(request.MagnetUrl, request.Category, request.SavePath, request.StartPaused);
        }
        else if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            if (request.DownloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                torrent = await _torrentService.AddFromMagnetAsync(request.DownloadUrl, request.Category, request.SavePath, request.StartPaused);
            }
            else
            {
                var bytes = await HttpClient.GetByteArrayAsync(request.DownloadUrl);
                var parsed = _torrentFileParser.Parse(bytes);
                torrent = await _torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, request.StartPaused, bytes);
            }
        }

        if (torrent == null)
        {
            return BadRequest("Failed to grab release.");
        }

        return Ok(TorrentResourceMapper.ToResource(torrent));
    }

    private async Task<ActionResult<IndexerTestResult>> TestDirectInternal(IndexerDefinition indexer)
    {
        try
        {
            var results = await _torznabClient.SearchAsync(indexer, string.Empty, limit: 1);
            return Ok(new IndexerTestResult
            {
                Success = true,
                Message = $"Connected successfully to {indexer.Name}."
            });
        }
        catch (Exception ex)
        {
            return Ok(new IndexerTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
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
            Tags = model.Tags ?? new List<int>()
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
            Tags = resource.Tags ?? new List<int>()
        };
    }
}
