// Copyright (c) PlaceholderCompany. All rights reserved.

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

[V1ApiController("ai")]
public class AiController : Controller
{
    private readonly IAiService aiService;
    private readonly IAiManager aiManager;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly IDownloadEngine downloadEngine;
    private readonly ITrackerEntryRepository trackerRepo;

    public AiController(
        IAiService aiService,
        IAiManager aiManager,
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        IDownloadEngine downloadEngine,
        ITrackerEntryRepository trackerRepo)
    {
        this.aiService = aiService;
        this.aiManager = aiManager;
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.downloadEngine = downloadEngine;
        this.trackerRepo = trackerRepo;
    }

    [HttpGet("status")]
    public async Task<ActionResult<object>> GetStatus()
    {
        var active = this.aiManager.ActiveProvider;
        var health = await this.aiManager.ProbeProviderAsync(this.aiManager.ActiveProviderId);

        return this.Ok(new
        {
            activeProviderId = this.aiManager.ActiveProviderId,
            displayName = active?.DisplayName ?? "None",
            version = active?.Version ?? "1.0",
            description = active?.Description ?? string.Empty,
            capabilities = new
            {
                supportsNaturalLanguageSearch = active?.Capabilities.HasFlag(AiCapabilities.SupportsNaturalLanguageSearch) ?? false,
                supportsReleaseNameParsing = active?.Capabilities.HasFlag(AiCapabilities.SupportsReleaseNameParsing) ?? false,
                supportsDiagnosticCopilot = active?.Capabilities.HasFlag(AiCapabilities.SupportsDiagnosticCopilot) ?? false,
                supportsMalwareAnomalyDetection = active?.Capabilities.HasFlag(AiCapabilities.SupportsMalwareAnomalyDetection) ?? false,
                supportsSwarmOptimization = active?.Capabilities.HasFlag(AiCapabilities.SupportsSwarmOptimization) ?? false,
                supportsLocalOfflineInference = active?.Capabilities.HasFlag(AiCapabilities.SupportsLocalOfflineInference) ?? false,
                supportsCloudLlm = active?.Capabilities.HasFlag(AiCapabilities.SupportsCloudLlm) ?? false,
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
            var torrent = this.torrentService.Get(request.TorrentId.Value);
            if (torrent != null)
            {
                torrentName ??= torrent.Name;
                var dbFiles = this.torrentFileService.GetFiles(request.TorrentId.Value);
                if (dbFiles != null)
                {
                    files.AddRange(dbFiles);
                }
            }
        }

        if (files.Count == 0 && request.FileNames != null && request.FileNames.Count > 0)
        {
            for (var i = 0; i < request.FileNames.Count; i++)
            {
                files.Add(new TorrentFile
                {
                    TorrentId = request.TorrentId ?? 0,
                    Path = request.FileNames[i],
                    Size = 1024 * 1024,
                });
            }
        }

        var assessment = await this.aiService.AnalyzeMalwareRiskAsync(torrentName ?? "Unknown", files);
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

        return this.Ok(new AiChatResponse
        {
            Success = true,
            Reply = reply,
            Provider = active?.DisplayName ?? this.aiManager.ActiveProviderId,
        });
    }
}
