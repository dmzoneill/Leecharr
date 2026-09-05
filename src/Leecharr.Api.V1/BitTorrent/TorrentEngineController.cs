// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.BitTorrent;

[V1ApiController("torrentengine")]
[Route("api/v1/config/engine")]
public class TorrentEngineController : Controller
{
    private readonly ITorrentEngineManager engineManager;
    private readonly IDownloadEngine downloadEngine;
    private readonly ITorrentService torrentService;

    public TorrentEngineController(
        ITorrentEngineManager engineManager,
        IDownloadEngine downloadEngine,
        ITorrentService torrentService)
    {
        this.engineManager = engineManager;
        this.downloadEngine = downloadEngine;
        this.torrentService = torrentService;
    }

    [HttpGet]
    public ActionResult<List<TorrentEngineResource>> GetEngines()
    {
        var activeId = this.engineManager.ActiveEngineId;
        var engines = this.engineManager.GetEngines().ToList();

        var resources = engines.Select(e => new TorrentEngineResource
        {
            EngineId = e.EngineId,
            DisplayName = e.DisplayName,
            Version = e.Version,
            IsActive = string.Equals(e.EngineId, activeId, StringComparison.OrdinalIgnoreCase),
            IsAvailable = e.IsAvailable,
            Status = string.Equals(e.EngineId, activeId, StringComparison.OrdinalIgnoreCase) ? "Running" : (e.IsAvailable ? "Ready" : "Unavailable"),
            Description = e.Description,
            Capabilities = e.Capabilities,
        }).ToList();

        return this.Ok(resources);
    }

    [HttpGet("active")]
    public ActionResult<ActiveEngineStatusResource> GetActiveEngine()
    {
        var active = this.engineManager.ActiveEngine;
        var allTasks = this.downloadEngine.GetAllTasks().ToList();

        var totalDownloadSpeed = allTasks.Sum(t => t.DownloadSpeed);
        var totalUploadSpeed = allTasks.Sum(t => t.UploadSpeed);
        var totalPeers = allTasks.Sum(t => t.ConnectedSeeders + t.ConnectedLeechers);

        return this.Ok(new ActiveEngineStatusResource
        {
            EngineId = active?.EngineId ?? "MonoTorrent",
            DisplayName = active?.DisplayName ?? "MonoTorrent",
            Version = active?.Version ?? "3.0.2",
            ActiveTorrentsCount = allTasks.Count,
            ConnectedPeersCount = totalPeers,
            DownloadSpeedBytes = totalDownloadSpeed,
            UploadSpeedBytes = totalUploadSpeed,
            ProtocolName = this.downloadEngine.ProtocolName,
        });
    }

    [HttpPost("switch")]
    public async Task<ActionResult<SwitchEngineResultResource>> SwitchEngine([FromBody] SwitchEngineRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EngineId))
        {
            return this.BadRequest(new SwitchEngineResultResource
            {
                Success = false,
                Error = "EngineId is required.",
            });
        }

        var result = await this.engineManager.SwitchEngineAsync(request.EngineId, request.PreserveTransfers);
        if (!result.Success)
        {
            return this.BadRequest(new SwitchEngineResultResource
            {
                Success = false,
                PreviousEngine = result.PreviousEngine,
                ActiveEngine = result.ActiveEngine,
                Error = result.Error,
            });
        }

        return this.Ok(new SwitchEngineResultResource
        {
            Success = true,
            PreviousEngine = result.PreviousEngine,
            ActiveEngine = result.ActiveEngine,
            TorrentsMigrated = result.TorrentsMigrated,
            Message = result.Message,
        });
    }

    [HttpPost("{engineId}/probe")]
    [HttpPost("probe/{engineId}")]
    public async Task<ActionResult<EngineProbeResultResource>> ProbeEngine(string engineId)
    {
        var probe = await this.engineManager.ProbeEngineAsync(engineId);
        return this.Ok(new EngineProbeResultResource
        {
            EngineId = engineId,
            IsHealthy = probe.IsHealthy,
            StatusMessage = probe.StatusMessage,
            DependencyChecks = probe.DependencyChecks,
            Warnings = probe.Warnings,
        });
    }

    [HttpPost("probe")]
    public async Task<ActionResult<EngineProbeResultResource>> ProbeEnginePost(
        [FromQuery] string engineId = null,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EngineProbeRequest request = null)
    {
        var targetEngine = !string.IsNullOrWhiteSpace(engineId)
            ? engineId
            : (!string.IsNullOrWhiteSpace(request?.EngineId)
                ? request.EngineId
                : (this.engineManager.ActiveEngineId ?? "MonoTorrent"));

        return await this.ProbeEngine(targetEngine);
    }

    [HttpGet("probe")]
    public async Task<ActionResult<EngineProbeResultResource>> ProbeEngineGet(
        [FromQuery] string engineId = null)
    {
        var targetEngine = !string.IsNullOrWhiteSpace(engineId)
            ? engineId
            : (this.engineManager.ActiveEngineId ?? "MonoTorrent");

        return await this.ProbeEngine(targetEngine);
    }
}
