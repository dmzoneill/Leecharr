using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Ai;

public class OllamaAiProvider : IAiEngineProvider, IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigService _configService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly RuleHeuristicAiProvider _fallbackProvider = new();
    private bool _disposed;

    public string ProviderId => "Ollama";
    public string DisplayName => "Ollama Local LLM Sidecar";
    public string Version => "1.0";
    public string Description => "Local Large Language Model provider connecting to an Ollama server (e.g., Llama 3, Mistral, Qwen, DeepSeek).";
    public bool IsAvailable => true;

    public AiCapabilities Capabilities =>
        AiCapabilities.SupportsNaturalLanguageSearch |
        AiCapabilities.SupportsReleaseNameParsing |
        AiCapabilities.SupportsDiagnosticCopilot |
        AiCapabilities.SupportsMalwareAnomalyDetection |
        AiCapabilities.SupportsLocalOfflineInference |
        AiCapabilities.SupportsCloudLlm;

    public OllamaAiProvider()
        : this(null, null, true)
    {
    }

    public OllamaAiProvider(IConfigService configService)
        : this(configService, null, true)
    {
    }

    public OllamaAiProvider(IConfigService configService, HttpClient httpClient, bool ownsHttpClient = false)
    {
        _configService = configService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _ownsHttpClient = ownsHttpClient || httpClient == null;
    }

    private string GetBaseUrl()
    {
        var env = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.TrimEnd('/');
        }

        var url = _configService?.GetValue("OllamaUrl", "http://127.0.0.1:11434") ?? "http://127.0.0.1:11434";
        return url.TrimEnd('/');
    }

    private string GetModelName()
    {
        return _configService?.GetValue("OllamaModel", "llama3.2") ?? "llama3.2";
    }

    public async Task<AiHealthResult> ProbeHealthAsync()
    {
        var baseUrl = GetBaseUrl();
        var model = GetModelName();
        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await _httpClient.GetAsync($"{baseUrl}/api/version", cts.Token);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new AiHealthResult
                {
                    IsHealthy = true,
                    StatusMessage = $"Ollama sidecar reachable at {baseUrl} using model '{model}'.",
                    LatencyMs = sw.ElapsedMilliseconds,
                    ModelName = model,
                    Version = Version
                };
            }

            return new AiHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Ollama sidecar returned HTTP status {(int)response.StatusCode} {response.ReasonPhrase}.",
                Warnings = new List<string> { $"Status code {(int)response.StatusCode}" },
                LatencyMs = sw.ElapsedMilliseconds,
                ModelName = model,
                Version = Version
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AiHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Ollama sidecar unreachable at {baseUrl}: {ex.Message}",
                Warnings = new List<string> { ex.Message },
                LatencyMs = sw.ElapsedMilliseconds,
                ModelName = model,
                Version = Version
            };
        }
    }

    public async Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        // Deterministic parser guarantees standard metadata extraction
        var result = await _fallbackProvider.ParseReleaseAsync(releaseName);
        result.AdditionalTags["Engine"] = "Ollama";
        return result;
    }

    public Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        return _fallbackProvider.DiagnoseTorrentHealthAsync(torrent, peers, trackers);
    }

    public Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery)
    {
        return _fallbackProvider.ProcessNaturalLanguageSearchAsync(naturalQuery);
    }

    public Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        return _fallbackProvider.AnalyzeMalwareRiskAsync(torrentName, files);
    }

    public async Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null)
    {
        var baseUrl = GetBaseUrl();
        var model = GetModelName();

        try
        {
            var payload = new
            {
                model = model,
                prompt = userMessage,
                system = systemContext ?? "You are Leecharr AI Assistant, an expert in BitTorrent protocol, Servarr integrations (*arr), swarm diagnostics, and media management.",
                stream = false
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await _httpClient.PostAsync($"{baseUrl}/api/generate", content, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("response", out var respProp))
                {
                    return respProp.GetString() ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Ollama chat generation failed, falling back to heuristic assistant.");
        }

        return await _fallbackProvider.GenerateChatResponseAsync(userMessage, systemContext);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
