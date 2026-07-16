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
    private readonly IIndexerRepository _repository;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public ProwlarrSyncService(IIndexerRepository repository, HttpClient httpClient = null)
    {
        _repository = repository;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _logger = LogManager.GetCurrentClassLogger();
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

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Failed to query Prowlarr indexers: HTTP {0}", response.StatusCode);
                return 0;
            }

            var json = await response.Content.ReadAsStringAsync();
            var indexers = JsonSerializer.Deserialize<List<ProwlarrIndexerDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (indexers == null || indexers.Count == 0)
            {
                return 0;
            }

            var syncedCount = 0;
            var existingIndexers = _repository.All().ToList();

            foreach (var pIndexer in indexers.Where(i => string.Equals(i.Protocol, "torrent", StringComparison.OrdinalIgnoreCase)))
            {
                var existing = existingIndexers.FirstOrDefault(e => string.Equals(e.Name, pIndexer.Name, StringComparison.OrdinalIgnoreCase));
                var feedUrl = $"{baseUri}/{pIndexer.Id}/api";

                if (existing == null)
                {
                    _repository.Insert(new IndexerDefinition
                    {
                        Name = pIndexer.Name,
                        Implementation = "Torznab",
                        Url = feedUrl,
                        ApiKey = apiKey,
                        Enable = pIndexer.Enable,
                        Priority = pIndexer.Priority
                    });
                }
                else
                {
                    existing.Url = feedUrl;
                    existing.ApiKey = apiKey;
                    existing.Enable = pIndexer.Enable;
                    existing.Priority = pIndexer.Priority;
                    _repository.Update(existing);
                }

                syncedCount++;
            }

            _logger.Info("Successfully synchronized {0} indexers from Prowlarr.", syncedCount);
            return syncedCount;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to sync indexers from Prowlarr.");
            return 0;
        }
    }
}
