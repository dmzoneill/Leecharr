using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.BitTorrent;

[V1ApiController("torrentengine")]
public class TorrentEngineController : Controller
{
    private readonly ITorrentEngineManager _engineManager;
    private readonly IDownloadEngine _downloadEngine;
    private readonly ITorrentService _torrentService;

    public TorrentEngineController(
        ITorrentEngineManager engineManager,
        IDownloadEngine downloadEngine,
        ITorrentService torrentService)
    {
        _engineManager = engineManager;
        _downloadEngine = downloadEngine;
        _torrentService = torrentService;
    }

    [HttpGet]
    public ActionResult<List<TorrentEngineResource>> GetEngines()
    {
        var activeId = _engineManager.ActiveEngineId;
        var engines = _engineManager.GetEngines().ToList();

        var resources = engines.Select(e => new TorrentEngineResource
        {
            EngineId = e.EngineId,
            DisplayName = e.DisplayName,
            Version = e.Version,
            IsActive = string.Equals(e.EngineId, activeId, StringComparison.OrdinalIgnoreCase),
            IsAvailable = e.IsAvailable,
            Status = string.Equals(e.EngineId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (e.IsAvailable ? "Ready" : "Emulated"),
            Description = e.Description,
            Capabilities = e.Capabilities
        }).ToList();

        return Ok(resources);
    }

    [HttpGet("active")]
    public ActionResult<ActiveEngineStatusResource> GetActiveEngine()
    {
        var active = _engineManager.ActiveEngine;
        var allTasks = _downloadEngine.GetAllTasks().ToList();

        var totalDownloadSpeed = allTasks.Sum(t => t.DownloadSpeed);
        var totalUploadSpeed = allTasks.Sum(t => t.UploadSpeed);
        var totalPeers = allTasks.Sum(t => t.ConnectedSeeders + t.ConnectedLeechers);

        return Ok(new ActiveEngineStatusResource
        {
            EngineId = active?.EngineId ?? "MonoTorrent",
            DisplayName = active?.DisplayName ?? "MonoTorrent",
            Version = active?.Version ?? "3.0.2",
            ActiveTorrentsCount = allTasks.Count,
            ConnectedPeersCount = totalPeers,
            DownloadSpeedBytes = totalDownloadSpeed,
            UploadSpeedBytes = totalUploadSpeed,
            ProtocolName = _downloadEngine.ProtocolName
        });
    }

    [HttpPost("switch")]
    public async Task<ActionResult<SwitchEngineResultResource>> SwitchEngine([FromBody] SwitchEngineRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EngineId))
        {
            return BadRequest(new SwitchEngineResultResource
            {
                Success = false,
                Error = "EngineId is required."
            });
        }

        var result = await _engineManager.SwitchEngineAsync(request.EngineId, request.PreserveTransfers);
        if (!result.Success)
        {
            return BadRequest(new SwitchEngineResultResource
            {
                Success = false,
                PreviousEngine = result.PreviousEngine,
                ActiveEngine = result.ActiveEngine,
                Error = result.Error
            });
        }

        return Ok(new SwitchEngineResultResource
        {
            Success = true,
            PreviousEngine = result.PreviousEngine,
            ActiveEngine = result.ActiveEngine,
            TorrentsMigrated = result.TorrentsMigrated,
            Message = result.Message
        });
    }

    [HttpPost("{engineId}/probe")]
    public async Task<ActionResult<EngineProbeResultResource>> ProbeEngine(string engineId)
    {
        var probe = await _engineManager.ProbeEngineAsync(engineId);
        return Ok(new EngineProbeResultResource
        {
            EngineId = engineId,
            IsHealthy = probe.IsHealthy,
            StatusMessage = probe.StatusMessage,
            DependencyChecks = probe.DependencyChecks,
            Warnings = probe.Warnings
        });
    }
}
