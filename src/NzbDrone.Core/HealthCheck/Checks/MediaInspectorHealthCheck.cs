// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.MediaInspection;

namespace NzbDrone.Core.HealthCheck.Checks;

public class MediaInspectorHealthCheck : IHealthCheck
{
    private readonly IMediaInspectorManager inspectorManager;

    public MediaInspectorHealthCheck(IMediaInspectorManager inspectorManager)
    {
        this.inspectorManager = inspectorManager;
    }

    public HealthCheckResult Check()
    {
        if (this.inspectorManager == null)
        {
            return HealthCheckResult.Error("MediaInspectorHealth", "No media inspector manager is configured or available.");
        }

        try
        {
            var activeProviderId = this.inspectorManager.ActiveProviderId;
            var result = this.inspectorManager.ProbeProviderAsync(activeProviderId).GetAwaiter().GetResult();
            if (result == null || !result.IsHealthy)
            {
                return HealthCheckResult.Error("MediaInspectorHealth", result?.StatusMessage ?? $"Media inspector provider '{activeProviderId}' is unhealthy.");
            }

            if (result.Warnings != null && result.Warnings.Count > 0)
            {
                return HealthCheckResult.Warning("MediaInspectorHealth", string.Join("; ", result.Warnings));
            }

            return HealthCheckResult.Ok("MediaInspectorHealth");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Error("MediaInspectorHealth", $"Media inspector health probe failed: {ex.Message}");
        }
    }
}
