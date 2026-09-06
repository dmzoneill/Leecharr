// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.BitTorrent;

namespace NzbDrone.Core.HealthCheck.Checks;

public class EngineHealthCheck : IHealthCheck
{
    private readonly IDownloadEngine engine;

    public EngineHealthCheck(IDownloadEngine engine)
    {
        this.engine = engine;
    }

    public HealthCheckResult Check()
    {
        if (this.engine == null)
        {
            return HealthCheckResult.Error("EngineHealth", "No download engine is configured or available.");
        }

        try
        {
            var result = this.engine.ProbeHealthAsync().GetAwaiter().GetResult();
            if (result == null || !result.IsHealthy)
            {
                return HealthCheckResult.Error("EngineHealth", result?.StatusMessage ?? "Download engine is unhealthy.");
            }

            if (result.Warnings != null && result.Warnings.Count > 0)
            {
                return HealthCheckResult.Warning("EngineHealth", string.Join("; ", result.Warnings));
            }

            return HealthCheckResult.Ok("EngineHealth");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Error("EngineHealth", $"Download engine health probe failed: {ex.Message}");
        }
    }
}
