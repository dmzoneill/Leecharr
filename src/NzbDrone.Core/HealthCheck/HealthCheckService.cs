using System;
using System.Collections.Generic;
using NLog;

namespace NzbDrone.Core.HealthCheck;

public interface IHealthCheckService
{
    List<HealthCheckResult> PerformChecks();
}

public class HealthCheckService : IHealthCheckService
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;
    private readonly Logger _logger;

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks)
    {
        _healthChecks = healthChecks;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<HealthCheckResult> PerformChecks()
    {
        var results = new List<HealthCheckResult>();
        foreach (var check in _healthChecks)
        {
            try
            {
                var result = check.Check();
                if (result.Type != HealthCheckResultType.Ok)
                {
                    _logger.Warn("Health check {0}: {1}", result.Source, result.Message);
                }

                results.Add(result);
            }
            catch (Exception ex)
            {
                var checkName = check.GetType().Name;
                _logger.Error(ex, "Health check {0} threw an unhandled exception", checkName);
                results.Add(HealthCheckResult.Error(checkName, $"Health check failed with exception: {ex.Message}"));
            }
        }

        return results;
    }
}
