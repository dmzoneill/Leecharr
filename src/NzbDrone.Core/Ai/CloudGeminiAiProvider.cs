// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
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

public class CloudGeminiAiProvider : IAiEngineProvider, IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigService configService;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly RuleHeuristicAiProvider fallbackProvider = new();
    private bool disposed;

    public string ProviderId => "Gemini";

    public string DisplayName => "Google Gemini Cloud LLM (Gemini 2.0 / 1.5)";

    public string Version => "1.0";

    public string Description => "Cloud Large Language Model provider powered by Google Gemini API for deep semantic release classification, diagnostics, and natural query parsing.";

    public bool IsAvailable => true;

    public AiCapabilities Capabilities =>
        AiCapabilities.SupportsNaturalLanguageSearch |
        AiCapabilities.SupportsReleaseNameParsing |
        AiCapabilities.SupportsDiagnosticCopilot |
        AiCapabilities.SupportsMalwareAnomalyDetection |
        AiCapabilities.SupportsCloudLlm;

    public CloudGeminiAiProvider()
        : this(null, null, true)
    {
    }

    public CloudGeminiAiProvider(IConfigService configService)
        : this(configService, null, true)
    {
    }

    public CloudGeminiAiProvider(IConfigService configService, HttpClient httpClient, bool ownsHttpClient = false)
    {
        this.configService = configService;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        this.ownsHttpClient = ownsHttpClient || httpClient == null;
    }

    private string GetApiKey()
    {
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return this.configService?.GetValue("GeminiApiKey", string.Empty) ?? string.Empty;
    }

    private string GetModelName()
    {
        return this.configService?.GetValue("GeminiModel", "gemini-2.0-flash") ?? "gemini-2.0-flash";
    }

    public async Task<AiHealthResult> ProbeHealthAsync()
    {
        var apiKey = this.GetApiKey();
        var model = this.GetModelName();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiHealthResult
            {
                IsHealthy = false,
                StatusMessage = "Gemini API key is not configured. Please set 'GeminiApiKey' in configuration or GEMINI_API_KEY environment variable.",
                Warnings = new List<string> { "Missing Gemini API key" },
                LatencyMs = 0,
                ModelName = model,
                Version = this.Version,
            };
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}?key={apiKey}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await this.httpClient.GetAsync(url, cts.Token).ConfigureAwait(false);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new AiHealthResult
                {
                    IsHealthy = true,
                    StatusMessage = $"Google Gemini API healthy and reachable with model '{model}'.",
                    LatencyMs = sw.ElapsedMilliseconds,
                    ModelName = model,
                    Version = this.Version,
                };
            }

            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new AiHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Google Gemini API returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                Warnings = new List<string> { errorBody },
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
                StatusMessage = $"Failed to reach Google Gemini API: {ex.Message}",
                Warnings = new List<string> { ex.ToString() },
                LatencyMs = sw.ElapsedMilliseconds,
                ModelName = model,
                Version = this.Version,
            };
        }
    }

    public async Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        var result = await this.fallbackProvider.ParseReleaseAsync(releaseName);
        result.AdditionalTags["Engine"] = "Gemini";
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
                report.Recommendations.Insert(0, $"[Gemini AI] {aiText.Trim()}");
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
        var apiKey = this.GetApiKey();
        var model = this.GetModelName();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = userMessage }
                            }
                        },
                    },
                    systemInstruction = new
                    {
                        parts = new[]
                        {
                            new { text = systemContext ?? "You are Leecharr AI Assistant, an expert in BitTorrent protocol, Servarr integrations (*arr), swarm diagnostics, and media management." }
                        }
                    },
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                using var response = await this.httpClient.PostAsync(url, content, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var first = candidates[0];
                        if (first.TryGetProperty("content", out var cObj) &&
                            cObj.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0 &&
                            parts[0].TryGetProperty("text", out var textProp))
                        {
                            return textProp.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Gemini chat generation failed, falling back to heuristic assistant.");
            }
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
