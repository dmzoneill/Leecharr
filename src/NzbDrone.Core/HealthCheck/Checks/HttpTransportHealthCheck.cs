// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Http.Transport;

namespace NzbDrone.Core.HealthCheck.Checks;

public class HttpTransportHealthCheck : IHealthCheck
{
    private readonly IHttpTransportManager httpTransportManager;

    public HttpTransportHealthCheck(IHttpTransportManager httpTransportManager)
    {
        this.httpTransportManager = httpTransportManager;
    }

    public HealthCheckResult Check()
    {
        if (this.httpTransportManager == null)
        {
            return HealthCheckResult.Error("HttpTransportHealth", "No HTTP transport manager is configured or available.");
        }

        try
        {
            var activeProviderId = this.httpTransportManager.ActiveProviderId;
            var result = this.httpTransportManager.ProbeProviderAsync(activeProviderId).GetAwaiter().GetResult();
            if (result == null || !result.IsHealthy)
            {
                return HealthCheckResult.Error("HttpTransportHealth", result?.StatusMessage ?? $"HTTP transport provider '{activeProviderId}' is unhealthy.");
            }

            if (result.Warnings != null && result.Warnings.Count > 0)
            {
                return HealthCheckResult.Warning("HttpTransportHealth", string.Join("; ", result.Warnings));
            }

            return HealthCheckResult.Ok("HttpTransportHealth");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Error("HttpTransportHealth", $"HTTP transport health probe failed: {ex.Message}");
        }
    }
}
