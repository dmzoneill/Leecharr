// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Http.Transport;

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
    Task<int> SyncFromProwlarrAsync(string prowlarrUrl, string apiKey);
}

public class ProwlarrSyncService : IProwlarrSyncService
{
    private readonly IIndexerRepository repository;
    private readonly HttpClient httpClient;
    private readonly ITorznabClient torznabClient;
    private readonly Logger logger;

    public ProwlarrSyncService(
        IIndexerRepository repository,
        IHttpTransportEngine transportEngine = null,
        HttpClient httpClient = null,
        ITorznabClient torznabClient = null)
    {
        this.repository = repository;
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
            this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        this.torznabClient = torznabClient;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public ProwlarrSyncService(IIndexerRepository repository, HttpClient httpClient, ITorznabClient torznabClient = null)
        : this(repository, null, httpClient, torznabClient)
    {
    }

    public async Task<int> SyncFromProwlarrAsync(string prowlarrUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(prowlarrUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return 0;
        }

        try
        {
            var baseUri = prowlarrUrl.TrimEnd('/');
            var requestUrl = $"{baseUri}/api/v1/indexer";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-Api-Key", apiKey);

            var response = await this.httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                this.logger.Warn("Failed to query Prowlarr indexers: HTTP {0}", response.StatusCode);
                return 0;
            }

            var json = await response.Content.ReadAsStringAsync();
            var indexers = JsonSerializer.Deserialize<List<ProwlarrIndexerDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (indexers == null || indexers.Count == 0)
            {
                return 0;
            }

            var torrentIndexers = indexers.Where(i => string.Equals(i.Protocol, "torrent", StringComparison.OrdinalIgnoreCase)).ToList();
            if (torrentIndexers.Count == 0)
            {
                var allExisting = this.repository.All().ToList();
                var prowlarrToDelete = allExisting
                    .Where(e => e.IsProwlarrManaged || e.ProwlarrIndexerId.HasValue)
                    .ToList();
                foreach (var indexerToPrune in prowlarrToDelete)
                {
                    this.logger.Info("Pruning deleted Prowlarr indexer: {0} (ProwlarrIndexerId: {1})", indexerToPrune.Name, indexerToPrune.ProwlarrIndexerId);
                    this.repository.Delete(indexerToPrune.Id);
                }

                return 0;
            }

            using var semaphore = new SemaphoreSlim(8);
            var tasks = torrentIndexers.Select(async pIndexer =>
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

                var existing = existingIndexers.FirstOrDefault(e => e.ProwlarrIndexerId == pIndexer.Id)
                               ?? existingIndexers.FirstOrDefault(e => e.ProwlarrIndexerId == null && string.Equals(e.Name, pIndexer.Name, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    this.repository.Insert(new IndexerDefinition
                    {
                        Name = pIndexer.Name,
                        Implementation = "Torznab",
                        Url = feedUrl,
                        ApiKey = apiKey,
                        Enable = pIndexer.Enable,
                        Priority = pIndexer.Priority,
                        EnableRss = pIndexer.EnableRss,
                        EnableSearch = pIndexer.EnableAutomaticSearch || pIndexer.EnableInteractiveSearch,
                        Categories = categories,
                        ProwlarrIndexerId = pIndexer.Id,
                        IsProwlarrManaged = true,
                    });
                }
                else
                {
                    existing.Name = pIndexer.Name;
                    existing.Url = feedUrl;
                    existing.ApiKey = apiKey;
                    existing.Enable = pIndexer.Enable;
                    existing.EnableRss = pIndexer.EnableRss;
                    existing.EnableSearch = pIndexer.EnableAutomaticSearch || pIndexer.EnableInteractiveSearch;
                    existing.ProwlarrIndexerId = pIndexer.Id;
                    existing.IsProwlarrManaged = true;

                    // Preserve local custom overrides: Priority, FreeleechOnly, MinSeeders, DownloadClientId, Tags
                    // and category filters if already configured locally
                    if (existing.Categories == null || existing.Categories.Count == 0)
                    {
                        existing.Categories = categories;
                    }

                    this.repository.Update(existing);
                }

                syncedCount++;
            }

            var toPrune = existingIndexers
                .Where(e => (e.IsProwlarrManaged || e.ProwlarrIndexerId.HasValue) && (!e.ProwlarrIndexerId.HasValue || !syncedProwlarrIds.Contains(e.ProwlarrIndexerId.Value)))
                .ToList();

            foreach (var indexerToPrune in toPrune)
            {
                this.logger.Info("Pruning deleted Prowlarr indexer: {0} (ProwlarrIndexerId: {1})", indexerToPrune.Name, indexerToPrune.ProwlarrIndexerId);
                this.repository.Delete(indexerToPrune.Id);
            }

            this.logger.Info("Successfully synchronized {0} indexers from Prowlarr.", syncedCount);
            return syncedCount;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to sync indexers from Prowlarr.");
            return 0;
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
