// Copyright (c) PlaceholderCompany. All rights reserved.

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

    private readonly IConfigService configService;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly RuleHeuristicAiProvider fallbackProvider = new();
    private bool disposed;

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
        this.configService = configService;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        this.ownsHttpClient = ownsHttpClient || httpClient == null;
    }

    private string GetBaseUrl()
    {
        var env = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.TrimEnd('/');
        }

        var url = !string.IsNullOrWhiteSpace(this.configService?.OllamaHost)
            ? this.configService.OllamaHost
            : this.configService?.GetValue("OllamaUrl", "http://127.0.0.1:11434") ?? "http://127.0.0.1:11434";
        return url.TrimEnd('/');
    }

    private string GetModelName()
    {
        return this.configService?.GetValue("OllamaModel", "llama3.2") ?? "llama3.2";
    }

    public async Task<AiHealthResult> ProbeHealthAsync()
    {
        var baseUrl = this.GetBaseUrl();
        var model = this.GetModelName();
        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await this.httpClient.GetAsync($"{baseUrl}/api/version", cts.Token);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new AiHealthResult
                {
                    IsHealthy = true,
                    StatusMessage = $"Ollama sidecar reachable at {baseUrl} using model '{model}'.",
                    LatencyMs = sw.ElapsedMilliseconds,
                    ModelName = model,
                    Version = this.Version,
                };
            }

            return new AiHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Ollama sidecar returned HTTP status {(int)response.StatusCode} {response.ReasonPhrase}.",
                Warnings = new List<string> { $"Status code {(int)response.StatusCode}" },
                LatencyMs = sw.ElapsedMilliseconds,
                ModelName = model,
                Version = this.Version,
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
                Version = this.Version,
            };
        }
    }

    public async Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        // Deterministic parser guarantees standard metadata extraction
        var result = await this.fallbackProvider.ParseReleaseAsync(releaseName);
        result.AdditionalTags["Engine"] = "Ollama";
        return result;
    }

    public async Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        var report = await this.fallbackProvider.DiagnoseTorrentHealthAsync(torrent, peers, trackers);
        try
        {
            var prompt = $"Analyze BitTorrent swarm: Name '{torrent?.Name}', Status '{torrent?.Status}', Progress {torrent?.Progress * 100:F1}%, Seeders {torrent?.Seeders}, Leechers {torrent?.Leechers}, DL Speed {torrent?.DownloadSpeed} B/s. Provide concise 1-sentence diagnostic.";
            var aiText = await this.GenerateChatResponseAsync(prompt, "You are a BitTorrent network diagnostics expert. Provide a single sentence diagnostic.");
            if (!string.IsNullOrWhiteSpace(aiText))
            {
                report.Recommendations.Insert(0, $"[Ollama AI] {aiText.Trim()}");
            }
        }
        catch
        {
        }

        return report;
    }

    public async Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery)
    {
        var result = await this.fallbackProvider.ProcessNaturalLanguageSearchAsync(naturalQuery);
        return result;
    }

    public async Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        var assessment = await this.fallbackProvider.AnalyzeMalwareRiskAsync(torrentName, files);
        return assessment;
    }

    public async Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null)
    {
        var baseUrl = this.GetBaseUrl();
        var model = this.GetModelName();

        try
        {
            var payload = new
            {
                model = model,
                prompt = userMessage,
                system = systemContext ?? "You are Leecharr AI Assistant, an expert in BitTorrent protocol, Servarr integrations (*arr), swarm diagnostics, and media management.",
                stream = false,
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await this.httpClient.PostAsync($"{baseUrl}/api/generate", content, cts.Token);

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

        return await this.fallbackProvider.GenerateChatResponseAsync(userMessage, systemContext);
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            if (this.ownsHttpClient)
            {
                this.httpClient.Dispose();
            }
        }
    }
}
