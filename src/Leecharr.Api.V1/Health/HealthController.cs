using System.Collections.Generic;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.HealthCheck;

namespace Leecharr.Api.V1.Health;

[V1ApiController("health")]
public class HealthController : Controller
{
    private readonly IHealthCheckService _healthCheckService;

    public HealthController(IHealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    public ActionResult<List<HealthCheckResult>> GetHealth()
    {
        return _healthCheckService.PerformChecks();
    }
}
