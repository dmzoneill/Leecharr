// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.MediaEnrichment.Providers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class MediaMetadataHealthCheck : IHealthCheck
{
    private readonly IMediaMetadataManager metadataManager;

    public MediaMetadataHealthCheck(IMediaMetadataManager metadataManager)
    {
        this.metadataManager = metadataManager;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (this.metadataManager == null)
        {
            return HealthCheckResult.Error("MediaMetadataHealth", "No media metadata manager is configured or available.");
        }

        try
        {
            var activeProviderId = this.metadataManager.ActiveProviderId;
            var result = await this.metadataManager.ProbeProviderAsync(activeProviderId).ConfigureAwait(false);
            if (result == null || !result.IsHealthy)
            {
                return HealthCheckResult.Error("MediaMetadataHealth", result?.StatusMessage ?? $"Media metadata provider '{activeProviderId}' is unhealthy.");
            }

            if (result.Warnings != null && result.Warnings.Count > 0)
            {
                return HealthCheckResult.Warning("MediaMetadataHealth", string.Join("; ", result.Warnings));
            }

            return HealthCheckResult.Ok("MediaMetadataHealth");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Error("MediaMetadataHealth", $"Media metadata health probe failed: {ex.Message}");
        }
    }
}
