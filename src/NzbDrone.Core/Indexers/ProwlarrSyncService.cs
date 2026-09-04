// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Indexers;

public class ProwlarrIndexerDto
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string Implementation { get; set; }

    public bool Enable { get; set; }

    public int Priority { get; set; }

    public string Protocol { get; set; }

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

    public ProwlarrSyncService(IIndexerRepository repository, HttpClient httpClient = null, ITorznabClient torznabClient = null)
    {
        this.repository = repository;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        this.torznabClient = torznabClient;
        this.logger = LogManager.GetCurrentClassLogger();
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

            var syncedCount = 0;
            var existingIndexers = this.repository.All().ToList();

            foreach (var pIndexer in indexers.Where(i => string.Equals(i.Protocol, "torrent", StringComparison.OrdinalIgnoreCase)))
            {
                var existing = existingIndexers.FirstOrDefault(e => string.Equals(e.Name, pIndexer.Name, StringComparison.OrdinalIgnoreCase));
                var feedUrl = $"{baseUri}/{pIndexer.Id}/api";
                var categories = await this.ExtractOrFetchCategoriesAsync(pIndexer, feedUrl, apiKey);

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
                        Categories = categories,
                    });
                }
                else
                {
                    existing.Url = feedUrl;
                    existing.ApiKey = apiKey;
                    existing.Enable = pIndexer.Enable;
                    existing.Priority = pIndexer.Priority;
                    if (categories.Count > 0 || existing.Categories == null || existing.Categories.Count == 0)
                    {
                        existing.Categories = categories;
                    }

                    this.repository.Update(existing);
                }

                syncedCount++;
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
