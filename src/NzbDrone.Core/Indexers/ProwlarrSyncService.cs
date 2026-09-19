// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Indexers;

public class ProwlarrIndexerDto
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string Implementation { get; set; }

    public bool Enable { get; set; }

    public int Priority { get; set; }

    public string Protocol { get; set; }

    public bool EnableRss { get; set; } = true;

    public bool EnableAutomaticSearch { get; set; } = true;

    public bool EnableInteractiveSearch { get; set; } = true;

    public List<ProwlarrFieldDto> Fields { get; set; } = new();

    public List<int> Categories { get; set; } = new();

    public ProwlarrCapabilitiesDto Capabilities { get; set; }
}

public class ProwlarrCapabilitiesDto
{
    public List<ProwlarrCategoryDto> Categories { get; set; } = new();
}

public class ProwlarrCategoryDto
{
    public int Id { get; set; }

    public string Name { get; set; }
}

public class ProwlarrFieldDto
{
    public string Name { get; set; }

    public object Value { get; set; }
}

public interface IProwlarrSyncService
{
    Task<int> SyncFromProwlarrAsync(string prowlarrUrl, string apiKey, bool? syncCategories = null);

    Task<int> SyncAllAsync();

    bool IsConfigured();
}

public class ProwlarrSyncService : IProwlarrSyncService, IExecute<ProwlarrSyncCommand>, IExecuteAsync<ProwlarrSyncCommand>
{
    private readonly IIndexerRepository repository;
    private readonly IArrConnectionRepository arrRepository;
    private readonly HttpClient httpClient;
    private readonly ITorznabClient torznabClient;
    private readonly SemaphoreSlim syncLock = new(1, 1);
    private readonly Logger logger;

    public ProwlarrSyncService(
        IIndexerRepository repository,
        IHttpTransportEngine transportEngine = null,
        HttpClient httpClient = null,
        ITorznabClient torznabClient = null,
        IArrConnectionRepository arrRepository = null)
    {
        this.repository = repository;
        this.arrRepository = arrRepository;
        if (httpClient != null)
        {
            this.httpClient = httpClient;
        }
        else if (transportEngine != null)
        {
            this.httpClient = new HttpClient(new DynamicHttpTransportHandler(transportEngine), disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
        }
        else
        {
            var handler = new SocketsHttpHandler();
            this.httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) };
        }

        this.torznabClient = torznabClient;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public ProwlarrSyncService(
        IIndexerRepository repository,
        HttpClient httpClient,
        ITorznabClient torznabClient = null,
        IArrConnectionRepository arrRepository = null)
        : this(repository, null, httpClient, torznabClient, arrRepository)
    {
    }

    public async Task ExecuteAsync(ProwlarrSyncCommand message, CancellationToken cancellationToken = default)
    {
        await this.SyncAllAsync();
    }

    public void Execute(ProwlarrSyncCommand message)
    {
        this.SyncAllAsync().GetAwaiter().GetResult();
    }

    public bool IsConfigured()
    {
        var hasIndexer = this.repository.All().Any(i =>
            !string.IsNullOrWhiteSpace(i.Url) &&
            !string.IsNullOrWhiteSpace(i.ApiKey) &&
            ((i.Implementation != null && i.Implementation.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrWhiteSpace(i.Name) && i.Name.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase)) ||
             i.Url.Contains("9696", StringComparison.OrdinalIgnoreCase)));

        if (hasIndexer)
        {
            return true;
        }

        if (this.arrRepository != null)
        {
            var hasArr = this.arrRepository.GetEnabled().Any(c =>
                string.Equals(c.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Url) &&
                !string.IsNullOrWhiteSpace(c.ApiKey));

            if (hasArr)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<int> SyncAllAsync()
    {
        var targets = new List<(string Url, string ApiKey, bool SyncCategories)>();

        var indexers = this.repository.All().Where(i =>
            !string.IsNullOrWhiteSpace(i.Url) &&
            !string.IsNullOrWhiteSpace(i.ApiKey) &&
            ((i.Implementation != null && i.Implementation.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrWhiteSpace(i.Name) && i.Name.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase)) ||
             i.Url.Contains("9696", StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var idx in indexers)
        {
            var url = idx.Url.Trim().TrimEnd('/');
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            {
                var path = Regex.Replace(parsed.AbsolutePath, @"/\d+/api/?$", string.Empty, RegexOptions.IgnoreCase);
                path = Regex.Replace(path, @"/api/?$", string.Empty, RegexOptions.IgnoreCase);
                url = $"{parsed.Scheme}://{parsed.Authority}{path.TrimEnd('/')}";
            }

            if (!targets.Any(t => string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add((url, idx.ApiKey, true));
            }
        }

        if (this.arrRepository != null)
        {
            var arrs = this.arrRepository.GetEnabled().Where(c =>
                string.Equals(c.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Url) &&
                !string.IsNullOrWhiteSpace(c.ApiKey)).ToList();

            foreach (var arr in arrs)
            {
                var url = arr.Url.Trim().TrimEnd('/');

                var existingTargetIndex = targets.FindIndex(t => string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase));
                if (existingTargetIndex >= 0)
                {
                    targets[existingTargetIndex] = (url, arr.ApiKey, arr.SyncCategories);
                }
                else
                {
                    targets.Add((url, arr.ApiKey, arr.SyncCategories));
                }
            }
        }

        if (targets.Count == 0)
        {
            this.logger.Debug("No Prowlarr instances configured to sync.");
            return 0;
        }

        var totalSynced = 0;
        foreach (var (url, apiKey, syncCategories) in targets)
        {
            var count = await this.SyncFromProwlarrAsync(url, apiKey, syncCategories);
            totalSynced += count;
        }

        return totalSynced;
    }

    public async Task<int> SyncFromProwlarrAsync(string prowlarrUrl, string apiKey, bool? syncCategories = null)
    {
        if (string.IsNullOrWhiteSpace(prowlarrUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return 0;
        }

        await this.syncLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var baseUri = prowlarrUrl.TrimEnd('/');

            var shouldSyncCategories = syncCategories;
            if (!shouldSyncCategories.HasValue && this.arrRepository != null)
            {
                var arr = this.arrRepository.GetEnabled().FirstOrDefault(c =>
                    string.Equals(c.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(c.Url?.TrimEnd('/'), baseUri, StringComparison.OrdinalIgnoreCase) ||
                     (Uri.TryCreate(c.Url, UriKind.Absolute, out var cu) && Uri.TryCreate(baseUri, UriKind.Absolute, out var bu) &&
                      string.Equals(cu.Authority, bu.Authority, StringComparison.OrdinalIgnoreCase))));
                if (arr != null)
                {
                    shouldSyncCategories = arr.SyncCategories;
                }
            }

            var doSyncCategories = shouldSyncCategories ?? true;
            var requestUrl = $"{baseUri}/api/v1/indexer";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-Api-Key", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await this.httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to connect to Prowlarr at {0}", requestUrl);
                throw;
            }

            string json;
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var msg = $"Prowlarr returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                    this.logger.Warn("Failed to query Prowlarr indexers: {0}", msg);
                    throw new HttpRequestException(msg, null, response.StatusCode);
                }

                json = await response.Content.ReadAsStringAsync();
            }

            var indexers = JsonSerializer.Deserialize<List<ProwlarrIndexerDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (indexers == null || indexers.Count == 0)
            {
                return 0;
            }

            var supportedIndexers = indexers.Where(i =>
                string.Equals(i.Protocol, "torrent", StringComparison.OrdinalIgnoreCase)).ToList();

            var skippedUsenetCount = indexers.Count(i => string.Equals(i.Protocol, "usenet", StringComparison.OrdinalIgnoreCase));
            if (skippedUsenetCount > 0)
            {
                this.logger.Info("Skipped {0} Usenet indexers from Prowlarr: Leecharr operates as a BitTorrent-only engine.", skippedUsenetCount);
            }

            if (supportedIndexers.Count == 0)
            {
                var allExisting = this.repository.All().ToList();
                var prowlarrToDelete = allExisting
                    .Where(e => (e.IsProwlarrManaged || e.ProwlarrIndexerId.HasValue || string.Equals(e.ConfigContract, "ProwlarrSettings", StringComparison.OrdinalIgnoreCase))
                                && !string.IsNullOrWhiteSpace(e.Url)
                                && e.Url.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var indexerToPrune in prowlarrToDelete)
                {
                    this.logger.Info("Pruning deleted Prowlarr indexer: {0} (ProwlarrIndexerId: {1})", indexerToPrune.Name, indexerToPrune.ProwlarrIndexerId);
                    TorznabClient.InvalidateCapabilities(indexerToPrune.Url, indexerToPrune.ApiKey);
                    this.repository.Delete(indexerToPrune.Id);
                }

                return 0;
            }

            using var semaphore = new SemaphoreSlim(8);
            var tasks = supportedIndexers.Select(async pIndexer =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var feedUrl = $"{baseUri}/{pIndexer.Id}/api";
                    var categories = await this.ExtractOrFetchCategoriesAsync(pIndexer, feedUrl, apiKey);
                    return (Indexer: pIndexer, FeedUrl: feedUrl, Categories: categories);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var processed = await Task.WhenAll(tasks);

            var existingIndexers = this.repository.All().ToList();
            var syncedProwlarrIds = new HashSet<int>();
            var syncedCount = 0;

            foreach (var item in processed)
            {
                var pIndexer = item.Indexer;
                var feedUrl = item.FeedUrl;
                var categories = item.Categories;

                syncedProwlarrIds.Add(pIndexer.Id);

                var implementation = !string.IsNullOrWhiteSpace(pIndexer.Implementation)
                    ? pIndexer.Implementation
                    : "Torznab";

                var existing = existingIndexers.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Url) && string.Equals(e.Url.TrimEnd('/'), feedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                                ?? existingIndexers.FirstOrDefault(e => e.ProwlarrIndexerId == pIndexer.Id && !string.IsNullOrWhiteSpace(e.Url) && e.Url.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase))
                                ?? existingIndexers.FirstOrDefault(e => (e.IsProwlarrManaged || string.Equals(e.ConfigContract, "ProwlarrSettings", StringComparison.OrdinalIgnoreCase)) && string.Equals(e.Name, pIndexer.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Url) && e.Url.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    var toInsert = new IndexerDefinition
                    {
                        Name = pIndexer.Name,
                        Implementation = implementation,
                        ConfigContract = "ProwlarrSettings",
                        Url = feedUrl,
                        ApiKey = apiKey,
                        Enable = pIndexer.Enable,
                        Priority = pIndexer.Priority,
                        EnableRss = pIndexer.EnableRss,
                        EnableSearch = pIndexer.EnableAutomaticSearch || pIndexer.EnableInteractiveSearch,
                        Categories = doSyncCategories ? categories : new List<int>(),
                        ProwlarrIndexerId = pIndexer.Id,
                        IsProwlarrManaged = true,
                    };

                    var inserted = this.repository.Insert(toInsert);
                    existingIndexers.Add(inserted ?? toInsert);
                }
                else
                {
                    TorznabClient.InvalidateCapabilities(existing.Url, existing.ApiKey);
                    if (!string.Equals(existing.Url, feedUrl, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existing.ApiKey, apiKey, StringComparison.OrdinalIgnoreCase))
                    {
                        TorznabClient.InvalidateCapabilities(feedUrl, apiKey);
                    }

                    existing.Name = pIndexer.Name;
                    if (!string.IsNullOrWhiteSpace(pIndexer.Implementation))
                    {
                        existing.Implementation = pIndexer.Implementation;
                    }
                    else if (string.IsNullOrWhiteSpace(existing.Implementation))
                    {
                        existing.Implementation = implementation;
                    }

                    existing.Url = feedUrl;
                    existing.ApiKey = apiKey;
                    existing.Enable = pIndexer.Enable;
                    existing.EnableRss = pIndexer.EnableRss;
                    existing.EnableSearch = pIndexer.EnableAutomaticSearch || pIndexer.EnableInteractiveSearch;
                    existing.ProwlarrIndexerId = pIndexer.Id;
                    existing.IsProwlarrManaged = true;
                    if (string.IsNullOrWhiteSpace(existing.ConfigContract))
                    {
                        existing.ConfigContract = "ProwlarrSettings";
                    }

                    // Respect SyncCategories setting
                    if (doSyncCategories)
                    {
                        existing.Categories = categories;
                    }
                    else if (existing.Categories == null)
                    {
                        existing.Categories = new List<int>();
                    }

                    this.repository.Update(existing);
                }

                syncedCount++;
            }

            var toPrune = existingIndexers
                .Where(e => (e.IsProwlarrManaged || e.ProwlarrIndexerId.HasValue || string.Equals(e.ConfigContract, "ProwlarrSettings", StringComparison.OrdinalIgnoreCase))
                            && !string.IsNullOrWhiteSpace(e.Url)
                            && e.Url.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase)
                            && (!e.ProwlarrIndexerId.HasValue || !syncedProwlarrIds.Contains(e.ProwlarrIndexerId.Value)))
                .ToList();

            foreach (var indexerToPrune in toPrune)
            {
                this.logger.Info("Pruning deleted Prowlarr indexer: {0} (ProwlarrIndexerId: {1})", indexerToPrune.Name, indexerToPrune.ProwlarrIndexerId);
                TorznabClient.InvalidateCapabilities(indexerToPrune.Url, indexerToPrune.ApiKey);
                this.repository.Delete(indexerToPrune.Id);
            }

            this.logger.Info("Successfully synchronized {0} indexers from Prowlarr.", syncedCount);
            return syncedCount;
        }
        finally
        {
            this.syncLock.Release();
        }
    }

    private async Task<List<int>> ExtractOrFetchCategoriesAsync(ProwlarrIndexerDto pIndexer, string feedUrl, string apiKey)
    {
        var categories = new HashSet<int>();

        if (pIndexer.Categories != null && pIndexer.Categories.Count > 0)
        {
            foreach (var c in pIndexer.Categories)
            {
                categories.Add(c);
            }
        }

        if (pIndexer.Capabilities?.Categories != null)
        {
            foreach (var c in pIndexer.Capabilities.Categories)
            {
                if (c.Id > 0)
                {
                    categories.Add(c.Id);
                }
            }
        }

        if (pIndexer.Fields != null)
        {
            foreach (var field in pIndexer.Fields)
            {
                if (string.Equals(field.Name, "categories", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(field.Name, "animeCategories", StringComparison.OrdinalIgnoreCase))
                {
                    if (field.Value is JsonElement jsonElement)
                    {
                        if (jsonElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in jsonElement.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var catId))
                                {
                                    categories.Add(catId);
                                }
                                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var parsedId))
                                {
                                    categories.Add(parsedId);
                                }
                            }
                        }
                        else if (jsonElement.ValueKind == JsonValueKind.String)
                        {
                            var parts = jsonElement.GetString()?.Split(',', StringSplitOptions.RemoveEmptyEntries);
                            if (parts != null)
                            {
                                foreach (var p in parts)
                                {
                                    if (int.TryParse(p.Trim(), out var parsedId))
                                    {
                                        categories.Add(parsedId);
                                    }
                                }
                            }
                        }
                    }
                    else if (field.Value is IEnumerable<int> intList)
                    {
                        foreach (var c in intList)
                        {
                            categories.Add(c);
                        }
                    }
                    else if (field.Value is string strVal)
                    {
                        var parts = strVal.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p.Trim(), out var parsedId))
                            {
                                categories.Add(parsedId);
                            }
                        }
                    }
                }
            }
        }

        if (categories.Count == 0 && this.torznabClient != null)
        {
            try
            {
                var probe = new IndexerDefinition
                {
                    Name = pIndexer.Name,
                    Url = feedUrl,
                    ApiKey = apiKey,
                };
                var caps = await this.torznabClient.FetchCapabilitiesAsync(probe);
                if (caps?.Categories != null)
                {
                    foreach (var cat in caps.Categories)
                    {
                        categories.Add(cat.Id);
                        foreach (var sub in cat.SubCategories)
                        {
                            categories.Add(sub.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to probe capabilities for Prowlarr indexer: {0}", pIndexer.Name);
            }
        }

        return categories.OrderBy(c => c).ToList();
    }
}
