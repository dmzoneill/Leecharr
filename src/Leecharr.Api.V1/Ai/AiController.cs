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
    private readonly IAiService _aiService;
    private readonly IAiManager _aiManager;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IDownloadEngine _downloadEngine;
    private readonly ITrackerEntryRepository _trackerRepo;

    public AiController(
        IAiService aiService,
        IAiManager aiManager,
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        IDownloadEngine downloadEngine,
        ITrackerEntryRepository trackerRepo)
    {
        _aiService = aiService;
        _aiManager = aiManager;
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _downloadEngine = downloadEngine;
        _trackerRepo = trackerRepo;
    }

    [HttpGet("status")]
    public async Task<ActionResult<object>> GetStatus()
    {
        var active = _aiManager.ActiveProvider;
        var health = await _aiManager.ProbeProviderAsync(_aiManager.ActiveProviderId);

        return Ok(new
        {
            activeProviderId = _aiManager.ActiveProviderId,
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
                supportsCloudLlm = active?.Capabilities.HasFlag(AiCapabilities.SupportsCloudLlm) ?? false
            },
            health
        });
    }

    [HttpPost("parse-release")]
    public async Task<ActionResult<AiParsedRelease>> ParseRelease([FromBody] AiParseReleaseRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ReleaseName))
        {
            return BadRequest(new { error = "ReleaseName is required." });
        }

        var result = await _aiService.ParseReleaseAsync(request.ReleaseName);
        return Ok(result);
    }

    [HttpPost("natural-search")]
    public async Task<ActionResult<AiSearchParameters>> ProcessNaturalSearch([FromBody] AiNaturalSearchRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "Query is required." });
        }

        var result = await _aiService.ProcessNaturalLanguageSearchAsync(request.Query);
        return Ok(result);
    }

    [HttpPost("diagnose/{torrentId:int}")]
    public async Task<ActionResult<AiDiagnosticReport>> DiagnoseTorrent(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound(new { error = $"Torrent with ID {torrentId} not found." });
        }

        var peers = new List<PeerInfo>();
        var task = _downloadEngine.GetTask(torrentId);
        if (task != null)
        {
            peers = task.GetPeers()?.ToList() ?? new List<PeerInfo>();
        }

        var trackers = _trackerRepo.GetByTorrentId(torrentId)?.ToList() ?? new List<TrackerEntry>();
        var report = await _aiService.DiagnoseTorrentHealthAsync(torrent, peers, trackers);

        return Ok(report);
    }

    [HttpPost("malware-check")]
    public async Task<ActionResult<AiMalwareRiskAssessment>> CheckMalwareRisk([FromBody] AiMalwareCheckRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request payload is required." });
        }

        var torrentName = request.TorrentName;
        var files = new List<TorrentFile>();

        if (request.TorrentId.HasValue)
        {
            var torrent = _torrentService.Get(request.TorrentId.Value);
            if (torrent != null)
            {
                torrentName ??= torrent.Name;
                var dbFiles = _torrentFileService.GetFiles(request.TorrentId.Value);
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
                    Size = 1024 * 1024
                });
            }
        }

        var assessment = await _aiService.AnalyzeMalwareRiskAsync(torrentName ?? "Unknown", files);
        return Ok(assessment);
    }

    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new AiChatResponse
            {
                Success = false,
                Error = "Message is required."
            });
        }

        var active = _aiManager.ActiveProvider;
        var reply = await _aiService.GenerateChatResponseAsync(request.Message, request.Context);

        return Ok(new AiChatResponse
        {
            Success = true,
            Reply = reply,
            Provider = active?.DisplayName ?? _aiManager.ActiveProviderId
        });
    }
}
