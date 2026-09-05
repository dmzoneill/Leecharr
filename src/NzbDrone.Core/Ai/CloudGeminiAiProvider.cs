// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
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

    private static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak != -1)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
            }

            if (trimmed.EndsWith("```"))
            {
                trimmed = trimmed[..^3];
            }

            trimmed = trimmed.Trim();
        }

        var startIdx = trimmed.IndexOf('{');
        var endIdx = trimmed.LastIndexOf('}');
        if (startIdx != -1 && endIdx > startIdx)
        {
            return trimmed.Substring(startIdx, endIdx - startIdx + 1);
        }

        return trimmed;
    }

    public async Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return await this.fallbackProvider.ParseReleaseAsync(releaseName);
        }

        var apiKey = this.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var systemPrompt = "You are a scene release title parsing engine. Output ONLY a raw JSON object with keys: cleanTitle (string), year (integer or null), resolution (string e.g. 1080p, 2160p, 720p), source (string e.g. WEB-DL, BluRay, HDTV), videoCodec (string e.g. x265, x264, HEVC, H.264), audioCodec (string e.g. AAC, DTS-HD, AC3), releaseGroup (string), season (integer or null), episode (integer or null), isProper (bool), isRepack (bool), confidenceScore (float 0.0-1.0). No markdown formatting or extra text.";
                var userPrompt = $"Parse this release name: \"{releaseName}\"";

                var responseText = await this.GenerateChatResponseAsync(userPrompt, systemPrompt);
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    var cleanJson = ExtractJson(responseText);
                    if (!string.IsNullOrWhiteSpace(cleanJson))
                    {
                        using var doc = JsonDocument.Parse(cleanJson);
                        var root = doc.RootElement;
                        var cleanTitle = root.TryGetProperty("cleanTitle", out var ct) ? ct.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(cleanTitle))
                        {
                            var parsed = new AiParsedRelease
                            {
                                RawTitle = releaseName,
                                CleanTitle = cleanTitle,
                                Year = root.TryGetProperty("year", out var yr) && yr.TryGetInt32(out var yVal) ? yVal : null,
                                Resolution = root.TryGetProperty("resolution", out var res) ? res.GetString() ?? string.Empty : string.Empty,
                                Source = root.TryGetProperty("source", out var src) ? src.GetString() ?? string.Empty : string.Empty,
                                VideoCodec = root.TryGetProperty("videoCodec", out var vc) ? vc.GetString() ?? string.Empty : string.Empty,
                                AudioCodec = root.TryGetProperty("audioCodec", out var ac) ? ac.GetString() ?? string.Empty : string.Empty,
                                ReleaseGroup = root.TryGetProperty("releaseGroup", out var rg) ? rg.GetString() ?? string.Empty : string.Empty,
                                Season = root.TryGetProperty("season", out var sn) && sn.TryGetInt32(out var snVal) ? snVal : (root.TryGetProperty("seasonNumber", out var snOld) && snOld.TryGetInt32(out var snOldVal) ? snOldVal : null),
                                Episode = root.TryGetProperty("episode", out var en) && en.TryGetInt32(out var enVal) ? enVal : (root.TryGetProperty("episodeNumber", out var enOld) && enOld.TryGetInt32(out var enOldVal) ? enOldVal : null),
                                IsProper = root.TryGetProperty("isProper", out var ip) && ip.GetBoolean(),
                                IsRepack = root.TryGetProperty("isRepack", out var ir) && ir.GetBoolean(),
                                ConfidenceScore = root.TryGetProperty("confidenceScore", out var cs) && cs.TryGetDouble(out var csVal) ? csVal : 0.95,
                            };
                            parsed.AdditionalTags["Engine"] = this.ProviderId;
                            return parsed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Gemini ParseReleaseAsync failed, falling back to heuristic engine.");
            }
        }

        return await this.fallbackProvider.ParseReleaseAsync(releaseName);
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
        if (string.IsNullOrWhiteSpace(naturalQuery))
        {
            return await this.fallbackProvider.ProcessNaturalLanguageSearchAsync(naturalQuery);
        }

        var apiKey = this.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var systemPrompt = "You are a natural language search parser for media indexers. Extract the user's intent and output ONLY a raw JSON object with keys: cleanTitle (string), rawQuery (string), year (integer or null), resolution (string e.g. 1080p, 2160p, 720p or null), source (string or null), season (integer or null), episode (integer or null), category (string e.g. movies, tv, music or null), minSeeders (integer). No markdown formatting or extra text.";
                var userPrompt = $"Convert this search query: \"{naturalQuery}\"";

                var responseText = await this.GenerateChatResponseAsync(userPrompt, systemPrompt);
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    var cleanJson = ExtractJson(responseText);
                    if (!string.IsNullOrWhiteSpace(cleanJson))
                    {
                        using var doc = JsonDocument.Parse(cleanJson);
                        var root = doc.RootElement;
                        var cleanTitle = root.TryGetProperty("cleanTitle", out var ct) ? ct.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(cleanTitle))
                        {
                            var searchParams = new AiSearchParameters
                            {
                                RawQuery = naturalQuery,
                                CleanTitle = cleanTitle,
                                Year = root.TryGetProperty("year", out var yr) && yr.TryGetInt32(out var yVal) ? yVal : null,
                                Resolution = root.TryGetProperty("resolution", out var res) ? res.GetString() : null,
                                Source = root.TryGetProperty("source", out var src) ? src.GetString() : null,
                                Season = root.TryGetProperty("season", out var sn) && sn.TryGetInt32(out var snVal) ? snVal : null,
                                Episode = root.TryGetProperty("episode", out var en) && en.TryGetInt32(out var enVal) ? enVal : null,
                                Category = root.TryGetProperty("category", out var cat) ? cat.GetString() : null,
                                MinSeeders = root.TryGetProperty("minSeeders", out var ms) && ms.TryGetInt32(out var msVal) ? msVal : 0,
                                ConfidenceScore = 0.95,
                            };
                            return searchParams;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Gemini ProcessNaturalLanguageSearchAsync failed, falling back to heuristic engine.");
            }
        }

        return await this.fallbackProvider.ProcessNaturalLanguageSearchAsync(naturalQuery);
    }

    public async Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        var apiKey = this.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var fileList = files != null ? string.Join(", ", files.Select(f => f.Path)) : "none";
                var systemPrompt = "You are a BitTorrent cybersecurity analysis engine. Assess the malware risk of the torrent name and file listing. Output ONLY a raw JSON object with keys: riskLevel (string: 'Safe', 'Suspicious', 'HighRisk'), riskScore (float 0.0-1.0), isSuspicious (bool), summary (string), suspiciousFiles (array of strings), threatReasons (array of strings), recommendations (array of strings). No markdown formatting or extra text.";
                var userPrompt = $"Analyze this torrent: Name=\"{torrentName}\", Files=[{fileList}]";

                var responseText = await this.GenerateChatResponseAsync(userPrompt, systemPrompt);
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    var cleanJson = ExtractJson(responseText);
                    if (!string.IsNullOrWhiteSpace(cleanJson))
                    {
                        using var doc = JsonDocument.Parse(cleanJson);
                        var root = doc.RootElement;
                        var riskLevel = root.TryGetProperty("riskLevel", out var rl) ? rl.GetString() ?? "Safe" : "Safe";
                        var riskScore = root.TryGetProperty("riskScore", out var rs) && rs.TryGetDouble(out var rsVal) ? rsVal : 0.0;
                        var isSuspicious = root.TryGetProperty("isSuspicious", out var susp) ? susp.GetBoolean() : (riskScore > 0.3);

                        var suspiciousFiles = new List<string>();
                        if (root.TryGetProperty("suspiciousFiles", out var sfArr) && sfArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in sfArr.EnumerateArray())
                            {
                                suspiciousFiles.Add(item.GetString() ?? string.Empty);
                            }
                        }

                        var threatReasons = new List<string>();
                        if (root.TryGetProperty("threatReasons", out var trArr) && trArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in trArr.EnumerateArray())
                            {
                                threatReasons.Add(item.GetString() ?? string.Empty);
                            }
                        }

                        var recommendations = new List<string>();
                        if (root.TryGetProperty("recommendations", out var recArr) && recArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in recArr.EnumerateArray())
                            {
                                recommendations.Add(item.GetString() ?? string.Empty);
                            }
                        }

                        return new AiMalwareRiskAssessment
                        {
                            TorrentName = torrentName ?? string.Empty,
                            RiskLevel = riskLevel,
                            RiskScore = riskScore,
                            IsSuspicious = isSuspicious,
                            AnalyzedFilesCount = files?.Count ?? 0,
                            SuspiciousFileNames = suspiciousFiles,
                            ThreatReasons = threatReasons,
                            Recommendations = recommendations,
                            AssessedAt = DateTime.UtcNow,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Gemini AnalyzeMalwareRiskAsync failed, falling back to heuristic engine.");
            }
        }

        return await this.fallbackProvider.AnalyzeMalwareRiskAsync(torrentName, files);
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
