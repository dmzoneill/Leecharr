// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IEnumerable<IHealthCheck> healthChecks;
    private readonly Logger logger;

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks)
    {
        this.healthChecks = healthChecks;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public List<HealthCheckResult> PerformChecks()
    {
        var results = new List<HealthCheckResult>();
        foreach (var check in this.healthChecks)
        {
            try
            {
                var result = check.Check();
                if (result.Type != HealthCheckResultType.Ok)
                {
                    this.logger.Warn("Health check {0}: {1}", result.Source, result.Message);
                }

                results.Add(result);
            }
            catch (Exception ex)
            {
                var checkName = check.GetType().Name;
                this.logger.Error(ex, "Health check {0} threw an unhandled exception", checkName);
                results.Add(HealthCheckResult.Error(checkName, $"Health check failed with exception: {ex.Message}"));
            }
        }

        return results;
    }
}
