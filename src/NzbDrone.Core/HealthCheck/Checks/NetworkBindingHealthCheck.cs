// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Network.Binding;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NetworkBindingHealthCheck : IHealthCheck
{
    private readonly INetworkBindingManager networkBindingManager;

    public NetworkBindingHealthCheck(INetworkBindingManager networkBindingManager)
    {
        this.networkBindingManager = networkBindingManager;
    }

    public HealthCheckResult Check()
    {
        if (this.networkBindingManager == null)
        {
            return HealthCheckResult.Error("NetworkBindingHealth", "No network binding manager is configured or available.");
        }

        try
        {
            var activeProviderId = this.networkBindingManager.ActiveProviderId;
            var result = this.networkBindingManager.ProbeProviderAsync(activeProviderId).GetAwaiter().GetResult();
            if (result == null || !result.IsHealthy)
            {
                return HealthCheckResult.Error("NetworkBindingHealth", result?.StatusMessage ?? $"Network binding provider '{activeProviderId}' is unhealthy.");
            }

            if (result.Warnings != null && result.Warnings.Count > 0)
            {
                return HealthCheckResult.Warning("NetworkBindingHealth", string.Join("; ", result.Warnings));
            }

            return HealthCheckResult.Ok("NetworkBindingHealth");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Error("NetworkBindingHealth", $"Network binding health probe failed: {ex.Message}");
        }
    }
}
