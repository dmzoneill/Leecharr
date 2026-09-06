// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Extraction;

namespace NzbDrone.Core.HealthCheck.Checks;

public class ExtractorHealthCheck : IHealthCheck
{
    private readonly IArchiveExtractorManager extractorManager;

    public ExtractorHealthCheck(IArchiveExtractorManager extractorManager)
    {
        this.extractorManager = extractorManager;
    }

    public HealthCheckResult Check()
    {
        if (this.extractorManager == null)
        {
            return HealthCheckResult.Error("ExtractorHealth", "No archive extractor manager is configured or available.");
        }

        try
        {
            var activeProviderId = this.extractorManager.ActiveProviderId;
            var result = this.extractorManager.ProbeProviderAsync(activeProviderId).GetAwaiter().GetResult();
            if (result == null || !result.IsHealthy)
            {
                return HealthCheckResult.Error("ExtractorHealth", result?.StatusMessage ?? $"Archive extractor provider '{activeProviderId}' is unhealthy.");
            }

            if (result.Warnings != null && result.Warnings.Count > 0)
            {
                return HealthCheckResult.Warning("ExtractorHealth", string.Join("; ", result.Warnings));
            }

            return HealthCheckResult.Ok("ExtractorHealth");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Error("ExtractorHealth", $"Archive extractor health probe failed: {ex.Message}");
        }
    }
}
