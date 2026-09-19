// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Telemetry;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/resources")]
public class SystemResourcesController : ControllerBase
{
    private readonly ISystemResourceService resourceService;

    public SystemResourcesController(ISystemResourceService resourceService)
    {
        this.resourceService = resourceService;
    }

    [HttpGet]
    public async Task<ActionResult<SystemResourceTelemetrySnapshot>> GetFullSnapshot(CancellationToken cancellationToken = default)
    {
        var snapshot = await this.resourceService.GetFullTelemetrySnapshotAsync(cancellationToken);
        return this.Ok(snapshot);
    }

    [HttpGet("host")]
    public ActionResult<HostProcessResourceMetrics> GetHostMetrics()
    {
        return this.Ok(this.resourceService.GetHostMetrics());
    }

    [HttpGet("engine")]
    public ActionResult<TorrentEngineMetrics> GetEngineMetrics()
    {
        return this.Ok(this.resourceService.GetTorrentEngineMetrics());
    }

    [HttpGet("subsystems")]
    public async Task<ActionResult<List<SubsystemTelemetryReport>>> GetSubsystemsTelemetry(CancellationToken cancellationToken = default)
    {
        var subsystems = await this.resourceService.GetSubsystemTelemetryAsync(null, cancellationToken);
        return this.Ok(subsystems);
    }

    [HttpGet("torrents")]
    public ActionResult<IReadOnlyList<TorrentResourceMetrics>> GetPerTorrentMetrics()
    {
        return this.Ok(this.resourceService.GetPerTorrentMetrics());
    }

    [HttpGet("torrents/{id:int}")]
    public ActionResult<TorrentResourceMetrics> GetTorrentMetrics(int id)
    {
        var metrics = this.resourceService.GetTorrentMetrics(id);
        if (metrics == null)
        {
            return this.NotFound(new { error = $"Torrent id {id} was not found in active engine session." });
        }

        return this.Ok(metrics);
    }
}
