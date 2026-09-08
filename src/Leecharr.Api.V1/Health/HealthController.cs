// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.HealthCheck;

namespace Leecharr.Api.V1.Health;

[V1ApiController("health")]
public class HealthController : Controller
{
    private readonly IHealthCheckService healthCheckService;

    public HealthController(IHealthCheckService healthCheckService)
    {
        this.healthCheckService = healthCheckService;
    }

    [HttpGet]
    public async Task<ActionResult<List<HealthCheckResult>>> GetHealth(CancellationToken cancellationToken = default)
    {
        var results = await this.healthCheckService.PerformChecksAsync(cancellationToken);
        return this.Ok(results);
    }
}
