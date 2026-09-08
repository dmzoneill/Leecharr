// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
    private readonly HttpClient httpClient;
    private readonly IDownloadHistoryService downloadHistoryService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public IndexerController(
        IIndexerRepository indexerRepository,
        ITorznabClient torznabClient,
        IProwlarrSyncService prowlarrSyncService,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ISafeHttpClientService safeHttpClientService = null,
        HttpClient httpClient = null,
        IDownloadHistoryService downloadHistoryService = null)
    {
        this.indexerRepository = indexerRepository;
        this.torznabClient = torznabClient;
        this.prowlarrSyncService = prowlarrSyncService;
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        this.downloadHistoryService = downloadHistoryService;
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
    public async Task<ActionResult<object>> SyncProwlarr(
        [FromBody] ProwlarrSyncRequest request = null,
        [FromQuery] string url = null,
        [FromQuery] string apiKey = null)
    {
        try
        {
            var targetUrl = !string.IsNullOrWhiteSpace(request?.Url) ? request.Url : (!string.IsNullOrWhiteSpace(url) ? url : null);
            var targetApiKey = !string.IsNullOrWhiteSpace(request?.ApiKey) ? request.ApiKey : (!string.IsNullOrWhiteSpace(apiKey) ? apiKey : null);

            if (targetUrl != null && targetApiKey != null)
            {
                var count = await this.prowlarrSyncService.SyncFromProwlarrAsync(targetUrl, targetApiKey);
                return this.Ok(new { success = true, syncedCount = count });
            }

            if (this.prowlarrSyncService.IsConfigured())
            {
                var count = await this.prowlarrSyncService.SyncAllAsync();
                return this.Ok(new { success = true, syncedCount = count });
            }

            if (string.IsNullOrWhiteSpace(targetApiKey))
            {
                return this.BadRequest(new { success = false, message = "Prowlarr API key is required." });
            }

            var defaultUrl = "http://localhost:9696";
            var directCount = await this.prowlarrSyncService.SyncFromProwlarrAsync(defaultUrl, targetApiKey);
            return this.Ok(new { success = true, syncedCount = directCount });
        }
        catch (HttpRequestException ex)
        {
            this.logger.Error(ex, "Failed to communicate with Prowlarr");
            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 502;
            if (statusCode == 400 || statusCode == 401 || statusCode == 403)
            {
                return this.BadRequest(new { success = false, message = ex.Message });
            }

            return this.StatusCode(502, new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to sync with Prowlarr");
            return this.BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<ReleaseInfoResource>>> SearchGet(
        [FromQuery] string query = null,
        [FromQuery] string category = null,
        [FromQuery] int? indexerId = null,
        [FromQuery] bool freeleechOnly = false,
        [FromQuery] int? season = null,
        [FromQuery] int? ep = null,
        [FromQuery] string imdbId = null,
        [FromQuery] string tmdbId = null,
        [FromQuery] string tvdbId = null,
        [FromQuery] string rid = null,
        [FromQuery] int? year = null,
        [FromQuery] string artist = null,
        [FromQuery] string album = null,
        [FromQuery] string author = null,
        [FromQuery] string isbn = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string type = null,
        CancellationToken cancellationToken = default)
    {
        return await this.ExecuteSearch(
            query,
            category,
            indexerId,
            freeleechOnly,
            season,
            ep,
            imdbId,
            tmdbId,
            tvdbId,
            rid,
            year,
            artist,
            album,
            author,
            isbn,
            offset,
            limit,
            type,
            cancellationToken);
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<ReleaseInfoResource>>> SearchPost(
        [FromBody] IndexerSearchRequest request = null,
        CancellationToken cancellationToken = default)
    {
        return await this.ExecuteSearch(
            request?.Query,
            request?.Category,
            request?.IndexerId,
            request?.FreeleechOnly ?? false,
            request?.Season,
            request?.Ep,
            request?.ImdbId,
            request?.TmdbId,
            request?.TvdbId,
            request?.Rid,
            request?.Year,
            request?.Artist,
            request?.Album,
            request?.Author,
            request?.Isbn,
            request?.Offset ?? 0,
            request?.Limit ?? 50,
            request?.Type,
            cancellationToken);
    }

    private async Task<ActionResult<List<ReleaseInfoResource>>> ExecuteSearch(
        string query,
        string category,
        int? indexerId,
        bool freeleechOnly,
        int? season,
        int? ep,
        string imdbId,
        string tmdbId,
        string tvdbId,
        string rid,
        int? year,
        string artist,
        string album,
        string author,
        string isbn,
        int offset,
        int limit,
        string type,
        CancellationToken cancellationToken = default)
    {
        var effectiveOffset = offset > 0 ? offset : 0;
        var effectiveLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 250);

        var searchEnabled = this.indexerRepository.GetSearchEnabled().ToList();
        var indexers = indexerId.HasValue
            ? new List<IndexerDefinition> { this.indexerRepository.Get(indexerId.Value) }.Where(i => i != null).ToList()
            : (searchEnabled.Count > 0 ? searchEnabled : this.indexerRepository.GetEnabled().ToList());

        if (indexers.Count == 0)
        {
            this.logger.Warn("Indexer search requested for query '{0}' but no search-enabled or active indexers are configured in repository.", query);
            if (this.Response?.Headers != null)
            {
                this.Response.Headers["X-Leecharr-Indexers-Configured"] = "0";
            }

            return this.Ok(new List<ReleaseInfoResource>());
        }

        var catId = ParseCategoryId(category);
        var isMulti = indexers.Count > 1;
        var fetchLimit = isMulti ? Math.Min(effectiveOffset + effectiveLimit, 200) : effectiveLimit;
        var fetchOffset = isMulti ? 0 : effectiveOffset;

        using var semaphore = new SemaphoreSlim(6);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var allResults = new ConcurrentBag<ReleaseInfoResource>();
        var searchTasks = indexers.Select(async idx =>
        {
            await semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            using var perIndexerCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token, perIndexerCts.Token);

            try
            {
                var results = await this.torznabClient.SearchAsync(
                    idx,
                    query ?? string.Empty,
                    catId,
                    fetchLimit,
                    fetchOffset,
                    season,
                    ep,
                    imdbId,
                    tmdbId,
                    type,
                    tvdbId,
                    rid,
                    year,
                    artist,
                    album,
                    author,
                    isbn,
                    combinedCts.Token).ConfigureAwait(false);

                foreach (var r in results)
                {
                    allResults.Add(new ReleaseInfoResource
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
                    });
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.Warn("Search timed out or cancelled for indexer {0}", idx.Name);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to search indexer {0}", idx.Name);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(searchTasks).ConfigureAwait(false);

        var filteredResults = allResults.ToList();
        if (freeleechOnly)
        {
            filteredResults = filteredResults.Where(r => r.IsFreeleech).ToList();
        }

        var sortedResults = filteredResults.OrderByDescending(r => r.Seeders).ToList();
        var paginatedResults = isMulti
            ? sortedResults.Skip(effectiveOffset).Take(effectiveLimit).ToList()
            : sortedResults.Take(effectiveLimit).ToList();

        return this.Ok(paginatedResults);
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
                try
                {
                    var bytes = await this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl);
                    var parsed = this.torrentFileParser.Parse(bytes);
                    torrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, request.StartPaused, bytes);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to download or parse .torrent for '{0}' from {1}", request.Title, request.DownloadUrl);
                }
            }
        }

        if (torrent == null && !string.IsNullOrWhiteSpace(request.InfoHash))
        {
            var fallbackMagnet = $"magnet:?xt=urn:btih:{request.InfoHash.Trim()}&dn={Uri.EscapeDataString(request.Title ?? request.InfoHash.Trim())}";
            try
            {
                torrent = await this.torrentService.AddFromMagnetAsync(fallbackMagnet, request.Category, request.SavePath, request.StartPaused);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to add release '{0}' via fallback magnet for infohash {1}", request.Title, request.InfoHash);
            }
        }

        if (torrent == null)
        {
            return this.BadRequest("Failed to grab release.");
        }

        if (this.downloadHistoryService != null)
        {
            var indexerName = request.IndexerName;
            if (string.IsNullOrWhiteSpace(indexerName) && request.IndexerId.HasValue)
            {
                var indexer = this.indexerRepository?.Get(request.IndexerId.Value);
                indexerName = indexer?.Name;
            }

            this.downloadHistoryService.RecordTorrentAdded(
                torrent,
                source: !string.IsNullOrWhiteSpace(indexerName) ? indexerName : "Indexer",
                magnetUrl: request.MagnetUrl,
                downloadUrl: request.DownloadUrl,
                indexerName: indexerName);
        }

        return this.Ok(TorrentResourceMapper.ToResource(torrent));
    }

    private async Task<ActionResult<IndexerTestResult>> TestDirectInternal(IndexerDefinition indexer)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return this.Ok(new IndexerTestResult
            {
                Success = false,
                Message = "Indexer URL is required.",
            });
        }

        var isProwlarr = (!string.IsNullOrWhiteSpace(indexer.Implementation) && indexer.Implementation.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(indexer.Url) && indexer.Url.Contains("9696", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(indexer.Name) && indexer.Name.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase));

        if (isProwlarr)
        {
            try
            {
                var baseUri = indexer.Url.TrimEnd('/');
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUri}/api/v1/indexer");
                if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
                {
                    request.Headers.Add("X-Api-Key", indexer.ApiKey);
                }

                var response = await this.httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var indexers = JsonSerializer.Deserialize<List<JsonElement>>(json);
                        var count = indexers?.Count ?? 0;
                        return this.Ok(new IndexerTestResult
                        {
                            Success = true,
                            Message = $"Connected successfully to Prowlarr. Found {count} indexers.",
                        });
                    }
                    catch
                    {
                        return this.Ok(new IndexerTestResult
                        {
                            Success = true,
                            Message = "Connected successfully to Prowlarr.",
                        });
                    }
                }

                using var statusReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUri}/api/v1/system/status");
                if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
                {
                    statusReq.Headers.Add("X-Api-Key", indexer.ApiKey);
                }

                var statusResp = await this.httpClient.SendAsync(statusReq);
                if (statusResp.IsSuccessStatusCode)
                {
                    return this.Ok(new IndexerTestResult
                    {
                        Success = true,
                        Message = "Connected successfully to Prowlarr.",
                    });
                }

                return this.Ok(new IndexerTestResult
                {
                    Success = false,
                    Message = $"Prowlarr returned HTTP {(int)response.StatusCode} {response.StatusCode}.",
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

        // For Torznab/Newznab indexers: test connection via ITorznabClient
        try
        {
            var testResult = await this.torznabClient.TestConnectionAsync(indexer);
            if (testResult.Success)
            {
                if (testResult.Capabilities?.Categories != null && testResult.Capabilities.Categories.Count > 0 &&
                    (indexer.Categories == null || indexer.Categories.Count == 0))
                {
                    var catIds = new HashSet<int>();
                    foreach (var cat in testResult.Capabilities.Categories)
                    {
                        catIds.Add(cat.Id);
                        foreach (var sub in cat.SubCategories)
                        {
                            catIds.Add(sub.Id);
                        }
                    }

                    indexer.Categories = catIds.OrderBy(c => c).ToList();
                }

                if (testResult.Capabilities != null)
                {
                    var settings = new IndexerSettings
                    {
                        SupportsSearch = testResult.Capabilities.SupportsSearch,
                        SupportsTvSearch = testResult.Capabilities.SupportsTvSearch,
                        SupportsMovieSearch = testResult.Capabilities.SupportsMovieSearch,
                        SupportsMusicSearch = testResult.Capabilities.SupportsMusicSearch,
                        SupportsBookSearch = testResult.Capabilities.SupportsBookSearch,
                        SupportedTvParams = testResult.Capabilities.SupportedTvParams,
                        SupportedMovieParams = testResult.Capabilities.SupportedMovieParams,
                        SupportedMusicParams = testResult.Capabilities.SupportedMusicParams,
                        SupportedBookParams = testResult.Capabilities.SupportedBookParams,
                        DefaultPageSize = testResult.Capabilities.DefaultPageSize,
                        MaxPageSize = testResult.Capabilities.MaxPageSize,
                    };
                    indexer.Settings = JsonSerializer.Serialize(settings);
                }

                if (indexer.Id > 0)
                {
                    this.indexerRepository.Update(indexer);
                }

                var msg = testResult.Capabilities != null
                    ? $"Connected successfully to {indexer.Name} (capabilities verified)."
                    : $"Connected successfully to {indexer.Name}.";

                return this.Ok(new IndexerTestResult
                {
                    Success = true,
                    Message = msg,
                });
            }

            return this.Ok(new IndexerTestResult
            {
                Success = false,
                Message = $"Connection failed: {testResult.ErrorMessage}",
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
        var res = new IndexerResource
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
            ProwlarrIndexerId = model.ProwlarrIndexerId,
            IsProwlarrManaged = model.IsProwlarrManaged,
        };

        if (!string.IsNullOrWhiteSpace(model.Settings))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<IndexerSettings>(model.Settings, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (settings != null)
                {
                    res.SupportsSearch = settings.SupportsSearch;
                    res.SupportsTvSearch = settings.SupportsTvSearch;
                    res.SupportsMovieSearch = settings.SupportsMovieSearch;
                    res.SupportsMusicSearch = settings.SupportsMusicSearch;
                    res.SupportsBookSearch = settings.SupportsBookSearch;
                    res.SupportedTvParams = settings.SupportedTvParams ?? new();
                    res.SupportedMovieParams = settings.SupportedMovieParams ?? new();
                    res.SupportedMusicParams = settings.SupportedMusicParams ?? new();
                    res.SupportedBookParams = settings.SupportedBookParams ?? new();
                    if (settings.DefaultPageSize > 0)
                    {
                        res.DefaultPageSize = settings.DefaultPageSize;
                    }

                    if (settings.MaxPageSize > 0)
                    {
                        res.MaxPageSize = settings.MaxPageSize;
                    }
                }
            }
            catch
            {
                // Ignore settings parsing errors
            }
        }

        return res;
    }

    private static IndexerDefinition ToModel(IndexerResource resource)
    {
        var settingsJson = resource.Settings;
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            var settings = new IndexerSettings
            {
                SupportsSearch = resource.SupportsSearch,
                SupportsTvSearch = resource.SupportsTvSearch,
                SupportsMovieSearch = resource.SupportsMovieSearch,
                SupportsMusicSearch = resource.SupportsMusicSearch,
                SupportsBookSearch = resource.SupportsBookSearch,
                SupportedTvParams = resource.SupportedTvParams ?? new(),
                SupportedMovieParams = resource.SupportedMovieParams ?? new(),
                SupportedMusicParams = resource.SupportedMusicParams ?? new(),
                SupportedBookParams = resource.SupportedBookParams ?? new(),
                DefaultPageSize = resource.DefaultPageSize > 0 ? resource.DefaultPageSize : 50,
                MaxPageSize = resource.MaxPageSize > 0 ? resource.MaxPageSize : 100,
            };
            settingsJson = JsonSerializer.Serialize(settings);
        }

        return new IndexerDefinition
        {
            Id = resource.Id,
            Name = resource.Name,
            Implementation = resource.Implementation ?? "Torznab",
            ConfigContract = resource.ConfigContract,
            Settings = settingsJson,
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
            ProwlarrIndexerId = resource.ProwlarrIndexerId,
            IsProwlarrManaged = resource.IsProwlarrManaged,
        };
    }

    private static int? ParseCategoryId(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var trimmed = category.Trim();
        if (int.TryParse(trimmed, out var parsedCat) && parsedCat > 0)
        {
            return parsedCat;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "movies" or "movie" => 2000,
            "tv" or "television" => 5000,
            "music" or "audio" => 3000,
            "anime" => 5070,
            "books" or "book" or "ebook" or "ebooks" => 7000,
            "apps" or "software" or "pc" => 4000,
            "games" or "game" or "console" => 1000,
            "other" or "misc" => 8000,
            _ => null,
        };
    }
}

public class ProwlarrSyncRequest
{
    public string Url { get; set; } = "http://localhost:9696";

    public string ApiKey { get; set; } = string.Empty;
}
