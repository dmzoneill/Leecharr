// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Ai;

public class OnnxLocalAiProvider : IAiEngineProvider, IFallbackAwareAiProvider
{
    private const string HeuristicEngineId = "RuleHeuristic";

    private readonly IConfigService configService;
    private readonly RuleHeuristicAiProvider fallbackProvider = new();

    public string ProviderId => "OnnxLocal";

    public string DisplayName => "Local ONNX / ML Inference Engine";

    public string Version => "1.0";

    public string Description => "Placeholder for local ONNX inference. No ONNX Runtime pipeline is implemented, so release parsing, natural language search, diagnostics, and malware scoring are not executed by this provider.";

    public bool IsAvailable => false;

    public bool LastChatUsedFallback => true;

    public AiCapabilities Capabilities => AiCapabilities.None;

    public OnnxLocalAiProvider()
        : this(null)
    {
    }

    public OnnxLocalAiProvider(IConfigService configService)
    {
        this.configService = configService;
    }

    public Task<AiHealthResult> ProbeHealthAsync()
    {
        var modelPath = this.configService?.GetValue("OnnxModelPath", "/config/models/leecharr-ai.onnx") ?? "/config/models/leecharr-ai.onnx";
        var modelExists = File.Exists(modelPath);
        var warnings = new List<string>
        {
            "ONNX Runtime inference is not implemented.",
        };

        if (modelExists)
        {
            warnings.Add($"A file exists at '{modelPath}', but it is not loaded or executed.");
        }
        else
        {
            warnings.Add($"Model file not found at '{modelPath}'.");
        }

        return Task.FromResult(new AiHealthResult
        {
            IsHealthy = false,
            StatusMessage = "ONNX local inference is unavailable because no ONNX Runtime pipeline is implemented.",
            Warnings = warnings,
            LatencyMs = 0,
            ModelName = "ONNX-Not-Implemented",
            Version = this.Version,
        });
    }

    public async Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        var result = await this.fallbackProvider.ParseReleaseAsync(releaseName);
        if (result != null)
        {
            result.AdditionalTags["Engine"] = HeuristicEngineId;
        }

        return result;
    }

    public Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        return this.fallbackProvider.DiagnoseTorrentHealthAsync(torrent, peers, trackers);
    }

    public Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery)
    {
        return this.fallbackProvider.ProcessNaturalLanguageSearchAsync(naturalQuery);
    }

    public Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        return this.fallbackProvider.AnalyzeMalwareRiskAsync(torrentName, files);
    }

    public Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null)
    {
        return this.fallbackProvider.GenerateChatResponseAsync(userMessage, systemContext);
    }
}
