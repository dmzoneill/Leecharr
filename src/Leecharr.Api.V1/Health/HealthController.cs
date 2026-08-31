// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
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
    public ActionResult<List<HealthCheckResult>> GetHealth()
    {
        return this.healthCheckService.PerformChecks();
    }
}
