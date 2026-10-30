// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Api.V1.Ai;

[V1ApiController("ai")] /* anonymous by design: emulated client login/discovery endpoint */ // nosemgrep
public class AiController : Controller
{
    private readonly IAiService aiService;
    private readonly IAiManager aiManager;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly IDownloadEngine downloadEngine;
    private readonly ITrackerEntryRepository trackerRepo;
    private readonly IAiMalwareAutoQuarantineService malwareAutoQuarantineService;

    public AiController(
        IAiService aiService,
        IAiManager aiManager,
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        IDownloadEngine downloadEngine,
        ITrackerEntryRepository trackerRepo,
        IAiMalwareAutoQuarantineService malwareAutoQuarantineService = null)
    {
        this.aiService = aiService;
        this.aiManager = aiManager;
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.downloadEngine = downloadEngine;
        this.trackerRepo = trackerRepo;
        this.malwareAutoQuarantineService = malwareAutoQuarantineService;
    }

    [HttpGet("status")]
    public async Task<ActionResult<object>> GetStatus()
    {
        var configured = this.aiManager.ActiveProvider;
        var configuredId = this.aiManager.ActiveProviderId;
        var configuredHealth = await this.aiManager.ProbeProviderAsync(configuredId);

        var executing = configured;
        var health = configuredHealth;
        if (configured != null && !configured.IsAvailable)
        {
            var heuristic = this.aiManager.GetProvider("RuleHeuristic");
            if (heuristic != null && heuristic.IsAvailable)
            {
                executing = heuristic;
                var heuristicHealth = await heuristic.ProbeHealthAsync();
                health = new AiHealthResult
                {
                    IsHealthy = heuristicHealth.IsHealthy,
                    StatusMessage = $"Configured provider '{configured.ProviderId}' does not execute AI. {configuredHealth.StatusMessage} Results are produced by {heuristic.ProviderId}.",
                    Warnings = configuredHealth.Warnings ?? new List<string>(),
                    LatencyMs = heuristicHealth.LatencyMs,
                    ModelName = heuristicHealth.ModelName,
                    Version = heuristicHealth.Version,
                };
            }
        }

        return this.Ok(new
        {
            activeProviderId = executing?.ProviderId ?? configuredId,
            displayName = executing?.DisplayName ?? "None",
            version = executing?.Version ?? "1.0",
            description = executing?.Description ?? string.Empty,
            capabilities = new
            {
                supportsNaturalLanguageSearch = executing?.Capabilities.HasFlag(AiCapabilities.SupportsNaturalLanguageSearch) ?? false,
                supportsReleaseNameParsing = executing?.Capabilities.HasFlag(AiCapabilities.SupportsReleaseNameParsing) ?? false,
                supportsDiagnosticCopilot = executing?.Capabilities.HasFlag(AiCapabilities.SupportsDiagnosticCopilot) ?? false,
                supportsMalwareAnomalyDetection = executing?.Capabilities.HasFlag(AiCapabilities.SupportsMalwareAnomalyDetection) ?? false,
                supportsSwarmOptimization = executing?.Capabilities.HasFlag(AiCapabilities.SupportsSwarmOptimization) ?? false,
                supportsLocalOfflineInference = executing?.Capabilities.HasFlag(AiCapabilities.SupportsLocalOfflineInference) ?? false,
                supportsCloudLlm = executing?.Capabilities.HasFlag(AiCapabilities.SupportsCloudLlm) ?? false,
            },
            health,
        });
    }

    [HttpPost("parse-release")]
    public async Task<ActionResult<AiParsedRelease>> ParseRelease([FromBody] AiParseReleaseRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ReleaseName))
        {
            return this.BadRequest(new { error = "ReleaseName is required." });
        }

        var result = await this.aiService.ParseReleaseAsync(request.ReleaseName);
        return this.Ok(result);
    }

    [HttpPost("natural-search")]
    public async Task<ActionResult<AiSearchParameters>> ProcessNaturalSearch([FromBody] AiNaturalSearchRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Query))
        {
            return this.BadRequest(new { error = "Query is required." });
        }

        var result = await this.aiService.ProcessNaturalLanguageSearchAsync(request.Query);
        return this.Ok(result);
    }

    [HttpPost("diagnose/{torrentId:int}")]
    public async Task<ActionResult<AiDiagnosticReport>> DiagnoseTorrent(int torrentId)
    {
        var torrent = this.torrentService.Get(torrentId);
        if (torrent == null)
        {
            return this.NotFound(new { error = $"Torrent with ID {torrentId} not found." });
        }

        var peers = new List<PeerInfo>();
        var task = this.downloadEngine.GetTask(torrentId);
        if (task != null)
        {
            peers = task.GetPeers()?.ToList() ?? new List<PeerInfo>();
        }

        var trackers = this.trackerRepo.GetByTorrentId(torrentId)?.ToList() ?? new List<TrackerEntry>();
        var report = await this.aiService.DiagnoseTorrentHealthAsync(torrent, peers, trackers);

        return this.Ok(report);
    }

    [HttpPost("malware-check")]
    public async Task<ActionResult<AiMalwareRiskAssessment>> CheckMalwareRisk([FromBody] AiMalwareCheckRequest request)
    {
        if (request == null)
        {
            return this.BadRequest(new { error = "Request payload is required." });
        }

        var torrentName = request.TorrentName;
        var files = new List<TorrentFile>();

        if (request.TorrentId.HasValue)
        {
            var torrentId = request.TorrentId.Value;
            var torrent = this.torrentService.Get(torrentId);
            if (torrent == null)
            {
                return this.NotFound(new { error = $"Torrent with ID {torrentId} not found." });
            }

            torrentName ??= torrent.Name;
            var dbFiles = this.torrentFileService.GetFiles(torrentId);
            if (dbFiles != null)
            {
                files.AddRange(dbFiles);
            }
        }

        if (request.FileNames != null && request.FileNames.Count > 0)
        {
            for (var i = 0; i < request.FileNames.Count; i++)
            {
                var size = 0L;
                if (request.FileSizes != null && i < request.FileSizes.Count)
                {
                    size = Math.Max(0L, request.FileSizes[i]);
                }

                files.Add(new TorrentFile
                {
                    TorrentId = request.TorrentId ?? 0,
                    Path = request.FileNames[i],
                    Size = size,
                });
            }
        }

        var assessment = await this.aiService.AnalyzeMalwareRiskAsync(torrentName ?? "Unknown", files);

        if (request.TorrentId.HasValue && this.malwareAutoQuarantineService != null)
        {
            await this.malwareAutoQuarantineService.TryAutoQuarantineAsync(request.TorrentId.Value, assessment);
        }

        return this.Ok(assessment);
    }

    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return this.BadRequest(new AiChatResponse
            {
                Success = false,
                Error = "Message is required.",
            });
        }

        var active = this.aiManager.ActiveProvider;
        var reply = await this.aiService.GenerateChatResponseAsync(request.Message, request.Context);

        var providerName = active?.DisplayName ?? this.aiManager.ActiveProviderId;
        var isFallback = false;

        if (this.aiService is IFallbackAwareAiProvider fallbackService && fallbackService.LastChatUsedFallback)
        {
            isFallback = true;
        }
        else if (this.aiManager is IFallbackAwareAiProvider fallbackManager && fallbackManager.LastChatUsedFallback)
        {
            isFallback = true;
        }
        else if (active is IFallbackAwareAiProvider fallbackProvider && fallbackProvider.LastChatUsedFallback)
        {
            isFallback = true;
        }

        if (isFallback && !string.Equals(active?.ProviderId, "RuleHeuristic", StringComparison.OrdinalIgnoreCase))
        {
            providerName = $"{providerName} (Fallback Heuristics)";
        }

        return this.Ok(new AiChatResponse
        {
            Success = true,
            Reply = reply,
            Provider = providerName,
        });
    }
}
