// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Api.V1.Torrents;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Indexers;

[V1ApiController("indexers")]
[Route("api/v1/indexer")]
[Authorize(Policy = "RequireOperator")]
public class IndexerController : Controller
{
    private static readonly Regex MagnetBtihRegex = new(@"urn:btih:([a-fA-F0-9]{40}|[a-zA-Z2-7]{32})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IIndexerRepository indexerRepository;
    private readonly ITorznabClient torznabClient;
    private readonly IProwlarrSyncService prowlarrSyncService;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly HttpClient httpClient;
    private readonly IDownloadHistoryService downloadHistoryService;
    private readonly IIndexerStatusService indexerStatusService;
    private readonly ITorrentRepository torrentRepository;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public IndexerController(
        IIndexerRepository indexerRepository,
        ITorznabClient torznabClient,
        IProwlarrSyncService prowlarrSyncService,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ISafeHttpClientService safeHttpClientService = null,
        HttpClient httpClient = null,
        IDownloadHistoryService downloadHistoryService = null,
        IIndexerStatusService indexerStatusService = null,
        ITorrentRepository torrentRepository = null)
    {
        this.indexerRepository = indexerRepository;
        this.torznabClient = torznabClient;
        this.prowlarrSyncService = prowlarrSyncService;
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        this.downloadHistoryService = downloadHistoryService;
        this.indexerStatusService = indexerStatusService ?? new IndexerStatusService();
        this.torrentRepository = torrentRepository;
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

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Indexer name is required.");
        }

        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return this.BadRequest("Indexer URL is required.");
        }

        if (!Uri.TryCreate(resource.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return this.BadRequest("Indexer URL must be a valid absolute HTTP or HTTPS URL.");
        }

        resource.Name = resource.Name.Trim();
        resource.Url = resource.Url.Trim();
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

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Indexer name is required.");
        }

        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return this.BadRequest("Indexer URL is required.");
        }

        if (!Uri.TryCreate(resource.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return this.BadRequest("Indexer URL must be a valid absolute HTTP or HTTPS URL.");
        }

        var existing = this.indexerRepository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        if (resource.ApiKey == "********" || (resource.ApiKey != null && resource.ApiKey.Contains('*')) || string.IsNullOrWhiteSpace(resource.ApiKey))
        {
            resource.ApiKey = existing.ApiKey;
        }

        TorznabClient.InvalidateCapabilities(existing.Url, existing.ApiKey);
        if (!string.Equals(existing.Url, resource.Url, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existing.ApiKey, resource.ApiKey, StringComparison.OrdinalIgnoreCase))
        {
            TorznabClient.InvalidateCapabilities(resource.Url, resource.ApiKey);
        }

        resource.Name = resource.Name.Trim();
        resource.Url = resource.Url.Trim();
        var model = ToModel(resource);
        model.Id = id;
        if (model.ApiKey == "********" || (model.ApiKey != null && model.ApiKey.Contains('*')) || string.IsNullOrWhiteSpace(model.ApiKey))
        {
            model.ApiKey = existing.ApiKey;
        }

        this.indexerRepository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        var existing = this.indexerRepository.Get(id);
        if (existing != null)
        {
            TorznabClient.InvalidateCapabilities(existing.Url, existing.ApiKey);
        }

        this.indexerRepository.Delete(id);
        this.indexerStatusService?.Reset(id);
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
        if (resource.Id > 0 && (resource.ApiKey == "********" || (resource.ApiKey != null && resource.ApiKey.Contains('*')) || string.IsNullOrWhiteSpace(resource.ApiKey)))
        {
            var existing = this.indexerRepository.Get(resource.Id);
            if (existing != null)
            {
                model.ApiKey = existing.ApiKey;
            }
        }

        return await this.TestDirectInternal(model);
    }

    [HttpPost("testall")]
    public async Task<ActionResult<List<IndexerBatchTestResult>>> TestAll()
    {
        var indexers = this.indexerRepository.All();
        if (indexers == null || !indexers.Any())
        {
            return this.Ok(new List<IndexerBatchTestResult>());
        }

        var results = new ConcurrentBag<IndexerBatchTestResult>();
        using var semaphore = new SemaphoreSlim(5);

        var tasks = indexers.Select(async idx =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            var sw = global::System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var testResult = await this.TestDirectInternal(idx).ConfigureAwait(false);
                sw.Stop();

                var success = false;
                string message = null;

                if (testResult.Result is OkObjectResult ok && ok.Value is IndexerTestResult tr)
                {
                    success = tr.Success;
                    message = tr.Message;
                }
                else if (testResult.Value is IndexerTestResult trVal)
                {
                    success = trVal.Success;
                    message = trVal.Message;
                }

                results.Add(new IndexerBatchTestResult
                {
                    Id = idx.Id,
                    Name = idx.Name,
                    Success = success,
                    Message = message ?? (success ? "Success" : "Failed"),
                    ResponseTimeMs = sw.ElapsedMilliseconds,
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new IndexerBatchTestResult
                {
                    Id = idx.Id,
                    Name = idx.Name,
                    Success = false,
                    Message = ex.Message,
                    ResponseTimeMs = sw.ElapsedMilliseconds,
                });
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return this.Ok(results.OrderBy(r => r.Id).ToList());
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
        [FromQuery] IndexerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return await this.ExecuteSearch(request, cancellationToken);
    }

    [NonAction]
    public Task<ActionResult<List<ReleaseInfoResource>>> SearchGet(
        string query = null,
        int? indexerId = null,
        string category = null,
        int limit = 50,
        int offset = 0,
        int? season = null,
        int? ep = null,
        string imdbId = null,
        string tmdbId = null,
        string type = null,
        string tvdbId = null,
        string rid = null,
        int? year = null,
        string artist = null,
        string album = null,
        string author = null,
        string isbn = null,
        bool freeleechOnly = false,
        CancellationToken cancellationToken = default)
    {
        var req = new IndexerSearchRequest
        {
            Query = query,
            IndexerId = indexerId,
            Category = category,
            Limit = limit,
            Offset = offset,
            Season = season,
            Ep = ep,
            ImdbId = imdbId,
            TmdbId = tmdbId,
            Type = type,
            TvdbId = tvdbId,
            Rid = rid,
            Year = year,
            Artist = artist,
            Album = album,
            Author = author,
            Isbn = isbn,
            FreeleechOnly = freeleechOnly,
        };

        return this.SearchGet(req, cancellationToken);
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<ReleaseInfoResource>>> SearchPost(
        [FromBody] IndexerSearchRequest request = null,
        CancellationToken cancellationToken = default)
    {
        return await this.ExecuteSearch(request, cancellationToken);
    }

    private async Task<ActionResult<List<ReleaseInfoResource>>> ExecuteSearch(
        IndexerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new IndexerSearchRequest();
        var effectiveOffset = request.Offset > 0 ? request.Offset : 0;
        var effectiveLimit = Math.Clamp(request.Limit <= 0 ? 50 : request.Limit, 1, 250);

        var searchEnabled = this.indexerRepository.GetSearchEnabled().ToList();
        var indexers = request.IndexerId.HasValue
            ? new List<IndexerDefinition> { this.indexerRepository.Get(request.IndexerId.Value) }.Where(i => i != null).ToList()
            : (searchEnabled.Count > 0 ? searchEnabled : this.indexerRepository.GetEnabled().ToList());

        if (this.indexerStatusService != null)
        {
            indexers = indexers.Where(i => !this.indexerStatusService.IsDisabled(i.Id)).ToList();
        }

        if (indexers.Count == 0)
        {
            this.logger.Warn("Indexer search requested for query '{0}' but no search-enabled or active indexers are configured in repository.", request.Query);
            if (this.Response?.Headers != null)
            {
                this.Response.Headers["X-Leecharr-Indexers-Configured"] = "0";
            }

            var emptyEnvelope = new IndexerSearchEnvelope()
            {
                Page = effectiveLimit > 0 ? (effectiveOffset / effectiveLimit) + 1 : 1,
                Limit = effectiveLimit,
                Total = 0,
            };
            return this.Ok(emptyEnvelope);
        }

        var parsedCategories = ParseCategories(request.Category);
        var catId = parsedCategories.Count > 0 ? parsedCategories[0] : (int?)null;
        var isMulti = indexers.Count > 1;
        var fetchLimit = isMulti
            ? (effectiveOffset > 0 && effectiveOffset < 100 ? Math.Min(effectiveOffset + effectiveLimit, 100) : Math.Min(effectiveLimit, 100))
            : effectiveLimit;
        var fetchOffset = isMulti && effectiveOffset < 100 ? 0 : effectiveOffset;

        using var semaphore = new SemaphoreSlim(6);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var allResults = new ConcurrentBag<ReleaseInfoResource>();
        var searchErrors = new ConcurrentBag<string>();
        var searchTasks = indexers.Select(async idx =>
        {
            await semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            using var perIndexerCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token, perIndexerCts.Token);

            try
            {
                var criteria = new TorznabSearchCriteria
                {
                    Query = request.Query ?? string.Empty,
                    CategoryId = catId,
                    Categories = parsedCategories,
                    Limit = fetchLimit,
                    Offset = fetchOffset,
                    Season = request.Season,
                    Ep = request.Ep,
                    ImdbId = request.ImdbId,
                    TmdbId = request.TmdbId,
                    SearchType = request.Type,
                    TvdbId = request.TvdbId,
                    Rid = request.Rid,
                    Year = request.Year,
                    Artist = request.Artist,
                    Album = request.Album,
                    Author = request.Author,
                    Isbn = request.Isbn,
                };

                var results = await this.torznabClient.SearchAsync(
                    idx,
                    criteria,
                    combinedCts.Token).ConfigureAwait(false);

                this.indexerStatusService?.RecordSuccess(idx.Id);

                foreach (var r in results)
                {
                    allResults.Add(new ReleaseInfoResource
                    {
                        Title = r.Title,
                        Guid = r.Guid,
                        Link = r.DownloadUrl ?? r.MagnetUrl,
                        Comments = r.Comments ?? string.Empty,
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
                        ResponseTotal = r.ResponseTotal,
                        ResponseOffset = r.ResponseOffset,
                        MinimumRatio = r.MinimumRatio,
                        MinimumSeedTime = r.MinimumSeedTime,
                    });
                }
            }
            catch (OperationCanceledException ex)
            {
                this.logger.Warn("Search timed out or cancelled for indexer {0}", idx.Name);
                this.indexerStatusService?.RecordFailure(idx.Id, errorMessage: "Search timed out", ex: ex);
                searchErrors.Add($"{idx.Name}: Search timed out");
            }
            catch (HttpRequestException ex)
            {
                this.logger.Warn(ex, "Failed to search indexer {0}", idx.Name);
                this.indexerStatusService?.RecordFailure(idx.Id, (int?)ex.StatusCode, ex.Message, ex);
                searchErrors.Add($"{idx.Name}: {ex.Message}");
            }
            catch (TorznabException ex)
            {
                this.logger.Warn(ex, "Torznab error for indexer {0}: {1}", idx.Name, ex.Message);
                this.indexerStatusService?.RecordFailure(idx.Id, ex.Code, ex.Message, ex);
                searchErrors.Add($"{idx.Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to search indexer {0}", idx.Name);
                this.indexerStatusService?.RecordFailure(idx.Id, errorMessage: ex.Message, ex: ex);
                searchErrors.Add($"{idx.Name}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(searchTasks).ConfigureAwait(false);

        if (this.Response?.Headers != null && !searchErrors.IsEmpty)
        {
            this.Response.Headers["X-Leecharr-Indexer-Errors"] = string.Join("; ", searchErrors);
        }

        var deduplicatedResults = DeduplicateReleases(allResults);

        var filteredResults = deduplicatedResults;
        if (request.FreeleechOnly)
        {
            filteredResults = filteredResults.Where(r => r.IsFreeleech).ToList();
        }

        var sortedResults = filteredResults
            .OrderByDescending(r => r.Seeders)
            .ThenByDescending(r => r.IsFreeleech)
            .ThenByDescending(r => r.PublishDate)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var paginatedResults = isMulti && fetchOffset == 0
            ? sortedResults.Skip(effectiveOffset).Take(effectiveLimit).ToList()
            : sortedResults.Take(effectiveLimit).ToList();

        var maxResponseTotal = deduplicatedResults.Select(r => r.ResponseTotal).Where(t => t.HasValue).Max() ?? 0;
        var totalCount = maxResponseTotal > 0 ? Math.Max(filteredResults.Count, maxResponseTotal) : filteredResults.Count;
        var currentPage = effectiveLimit > 0 ? (effectiveOffset / effectiveLimit) + 1 : 1;

        var envelope = new IndexerSearchEnvelope(paginatedResults)
        {
            Page = currentPage,
            Limit = effectiveLimit,
            Total = totalCount,
        };

        return this.Ok(envelope);
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
            else if (request.DownloadUrl.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
            {
                this.logger.Warn("Cannot grab NZB release '{0}' from {1}: Leecharr operates exclusively as a BitTorrent engine.", request.Title, request.DownloadUrl);
                return this.BadRequest("Usenet/NZB releases are not supported. Leecharr is a BitTorrent engine.");
            }
            else
            {
                try
                {
                    IDictionary<string, string> customHeaders = null;
                    var cookies = request.Cookie;
                    var userAgent = request.UserAgent;

                    if (request.IndexerId.HasValue && request.IndexerId.Value > 0)
                    {
                        var indexerDef = this.indexerRepository.Get(request.IndexerId.Value);
                        if (indexerDef != null)
                        {
                            if (!string.IsNullOrWhiteSpace(indexerDef.ApiKey))
                            {
                                customHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                customHeaders["X-Api-Key"] = indexerDef.ApiKey;
                            }

                            if (!string.IsNullOrWhiteSpace(indexerDef.Settings))
                            {
                                try
                                {
                                    var settings = JsonSerializer.Deserialize<IndexerSettings>(indexerDef.Settings, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                    if (settings != null)
                                    {
                                        if (string.IsNullOrWhiteSpace(cookies) && !string.IsNullOrWhiteSpace(settings.Cookie))
                                        {
                                            cookies = settings.Cookie;
                                        }

                                        if (string.IsNullOrWhiteSpace(userAgent) && !string.IsNullOrWhiteSpace(settings.UserAgent))
                                        {
                                            userAgent = settings.UserAgent;
                                        }
                                    }
                                }
                                catch
                                {
                                    // Ignore settings deserialization failure
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(cookies))
                    {
                        customHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        customHeaders["Cookie"] = cookies;
                    }

                    if (!string.IsNullOrWhiteSpace(userAgent))
                    {
                        customHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        customHeaders["User-Agent"] = userAgent;
                    }

                    var bytes = customHeaders != null && customHeaders.Count > 0
                        ? await this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl, customHeaders).ConfigureAwait(false)
                        : await this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl).ConfigureAwait(false);

                    if (bytes != null && bytes.Length > 0 && bytes[0] == (byte)'<')
                    {
                        this.logger.Warn("Downloaded payload for '{0}' from {1} appears to be XML/NZB rather than a .torrent file.", request.Title, request.DownloadUrl);
                        return this.BadRequest("Downloaded release payload is an XML/NZB file or web error, not a valid .torrent file. Leecharr is a BitTorrent engine.");
                    }

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
            var fallbackMagnet = MagnetLinkParser.BuildMagnetUri(request.InfoHash.Trim(), request.Title ?? request.InfoHash.Trim());
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

        var ratioUpdated = false;
        if (request.MinimumRatio.HasValue && request.MinimumRatio.Value > 0)
        {
            torrent.TargetRatio = Math.Max(torrent.TargetRatio, request.MinimumRatio.Value);
            ratioUpdated = true;
        }

        if (request.MinimumSeedTime.HasValue && request.MinimumSeedTime.Value > 0)
        {
            var seedTimeMinutes = (int)Math.Ceiling(request.MinimumSeedTime.Value / 60.0);
            torrent.TargetSeedTimeMinutes = Math.Max(torrent.TargetSeedTimeMinutes, seedTimeMinutes);
            ratioUpdated = true;
        }

        if (ratioUpdated)
        {
            this.torrentRepository?.Update(torrent);
            if (this.torrentService != null)
            {
                await this.torrentService.UpdateAsync(torrent);
            }
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

        try
        {
            this.safeHttpClientService.ValidateUrl(indexer.Url);
        }
        catch (Exception ex)
        {
            if (indexer.Id > 0)
            {
                this.indexerStatusService?.RecordFailure(indexer.Id, errorMessage: ex.Message, ex: ex);
            }

            return this.Ok(new IndexerTestResult
            {
                Success = false,
                Message = $"URL validation failed: {ex.Message}",
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
                        if (indexer.Id > 0)
                        {
                            this.indexerStatusService?.RecordSuccess(indexer.Id);
                        }

                        return this.Ok(new IndexerTestResult
                        {
                            Success = true,
                            Message = $"Connected successfully to Prowlarr. Found {count} indexers.",
                        });
                    }
                    catch
                    {
                        if (indexer.Id > 0)
                        {
                            this.indexerStatusService?.RecordSuccess(indexer.Id);
                        }

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
                    if (indexer.Id > 0)
                    {
                        this.indexerStatusService?.RecordSuccess(indexer.Id);
                    }

                    return this.Ok(new IndexerTestResult
                    {
                        Success = true,
                        Message = "Connected successfully to Prowlarr.",
                    });
                }

                if (indexer.Id > 0)
                {
                    this.indexerStatusService?.RecordFailure(indexer.Id, (int)response.StatusCode, $"Prowlarr returned HTTP {(int)response.StatusCode}");
                }

                return this.Ok(new IndexerTestResult
                {
                    Success = false,
                    Message = $"Prowlarr returned HTTP {(int)response.StatusCode} {response.StatusCode}.",
                });
            }
            catch (Exception ex)
            {
                if (indexer.Id > 0)
                {
                    this.indexerStatusService?.RecordFailure(indexer.Id, errorMessage: ex.Message, ex: ex);
                }

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
                if (indexer.Id > 0)
                {
                    this.indexerStatusService?.RecordSuccess(indexer.Id);
                }

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
                    var existingSettings = new IndexerSettings();
                    if (!string.IsNullOrWhiteSpace(indexer.Settings))
                    {
                        try
                        {
                            existingSettings = JsonSerializer.Deserialize<IndexerSettings>(indexer.Settings, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new IndexerSettings();
                        }
                        catch
                        {
                            // Ignore deserialize error
                        }
                    }

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
                        Cookie = existingSettings.Cookie,
                        UserAgent = existingSettings.UserAgent,
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

            if (indexer.Id > 0)
            {
                this.indexerStatusService?.RecordFailure(indexer.Id, errorMessage: testResult.ErrorMessage);
            }

            return this.Ok(new IndexerTestResult
            {
                Success = false,
                Message = $"Connection failed: {testResult.ErrorMessage}",
            });
        }
        catch (Exception ex)
        {
            if (indexer.Id > 0)
            {
                this.indexerStatusService?.RecordFailure(indexer.Id, errorMessage: ex.Message, ex: ex);
            }

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
            ApiKey = string.IsNullOrEmpty(model.ApiKey) ? string.Empty : "********",
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
                    res.Cookie = settings.Cookie;
                    res.UserAgent = settings.UserAgent;
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
                Cookie = resource.Cookie,
                UserAgent = resource.UserAgent,
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

    internal static List<ReleaseInfoResource> DeduplicateReleases(IEnumerable<ReleaseInfoResource> releases)
    {
        if (releases == null)
        {
            return new List<ReleaseInfoResource>();
        }

        var releasesList = releases.ToList();
        if (releasesList.Count <= 1)
        {
            return releasesList;
        }

        var titleSizeToHash = new Dictionary<(string, long), string>();
        foreach (var r in releasesList)
        {
            var hash = NormalizeInfoHash(r.InfoHash, r.MagnetUrl);
            if (!string.IsNullOrEmpty(hash) && !string.IsNullOrWhiteSpace(r.Title))
            {
                var key = (r.Title.Trim().ToLowerInvariant(), r.Size);
                titleSizeToHash.TryAdd(key, hash);
            }
        }

        var groups = releasesList.GroupBy(r =>
        {
            var hash = NormalizeInfoHash(r.InfoHash, r.MagnetUrl);
            if (string.IsNullOrEmpty(hash) && !string.IsNullOrWhiteSpace(r.Title))
            {
                titleSizeToHash.TryGetValue((r.Title.Trim().ToLowerInvariant(), r.Size), out hash);
            }

            if (!string.IsNullOrEmpty(hash))
            {
                return "hash:" + hash;
            }

            if (!string.IsNullOrWhiteSpace(r.Title))
            {
                return $"title:{r.Title.Trim().ToLowerInvariant()}_{r.Size}";
            }

            return "guid:" + (r.Guid ?? Guid.NewGuid().ToString());
        });

        var deduplicated = new List<ReleaseInfoResource>();
        foreach (var group in groups)
        {
            var primary = group
                .OrderByDescending(r => r.Seeders)
                .ThenByDescending(r => r.IsFreeleech)
                .ThenByDescending(r => !string.IsNullOrEmpty(r.DownloadUrl))
                .ThenByDescending(r => r.PublishDate)
                .First();

            primary.Seeders = group.Max(r => r.Seeders);
            primary.Leechers = group.Max(r => r.Leechers);
            primary.DownloadVolumeFactor = group.Min(r => r.DownloadVolumeFactor);

            if (string.IsNullOrWhiteSpace(primary.DownloadUrl))
            {
                primary.DownloadUrl = group.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.DownloadUrl))?.DownloadUrl;
            }

            if (string.IsNullOrWhiteSpace(primary.MagnetUrl))
            {
                primary.MagnetUrl = group.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.MagnetUrl))?.MagnetUrl;
            }

            if (string.IsNullOrWhiteSpace(primary.Link))
            {
                primary.Link = group.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Link))?.Link
                    ?? primary.DownloadUrl
                    ?? primary.MagnetUrl;
            }

            if (string.IsNullOrWhiteSpace(primary.Comments))
            {
                primary.Comments = group.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Comments))?.Comments ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(primary.InfoHash))
            {
                var resolvedHash = group.Select(r => NormalizeInfoHash(r.InfoHash, r.MagnetUrl)).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
                if (string.IsNullOrWhiteSpace(resolvedHash) && !string.IsNullOrWhiteSpace(primary.Title))
                {
                    titleSizeToHash.TryGetValue((primary.Title.Trim().ToLowerInvariant(), primary.Size), out resolvedHash);
                }

                if (!string.IsNullOrWhiteSpace(resolvedHash))
                {
                    primary.InfoHash = resolvedHash;
                }
            }

            deduplicated.Add(primary);
        }

        return deduplicated;
    }

    internal static string NormalizeInfoHash(string infoHash, string magnetUrl)
    {
        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            var trimmed = infoHash.Trim();
            var match = MagnetBtihRegex.Match(trimmed);
            if (match.Success)
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }

            return trimmed.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(magnetUrl))
        {
            try
            {
                var parsed = MagnetLinkParser.Parse(magnetUrl);
                if (!string.IsNullOrWhiteSpace(parsed.InfoHash))
                {
                    return parsed.InfoHash.ToLowerInvariant();
                }
            }
            catch
            {
            }

            var match = MagnetBtihRegex.Match(magnetUrl);
            if (match.Success)
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    internal static List<int> ParseCategories(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return new List<int>();
        }

        var results = new List<int>();
        var parts = category.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var id = ParseSingleCategoryId(part);
            if (id.HasValue && !results.Contains(id.Value))
            {
                results.Add(id.Value);
            }
        }

        return results;
    }

    internal static int? ParseCategoryId(string category)
    {
        var categories = ParseCategories(category);
        return categories.Count > 0 ? categories[0] : null;
    }

    private static int? ParseSingleCategoryId(string category)
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
