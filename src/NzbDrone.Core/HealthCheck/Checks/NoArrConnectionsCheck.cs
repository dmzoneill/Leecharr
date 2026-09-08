// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoArrConnectionsCheck : IHealthCheck
{
    private readonly IArrConnectionRepository arrRepo;

    public NoArrConnectionsCheck(IArrConnectionRepository arrRepo)
    {
        this.arrRepo = arrRepo;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var connections = this.arrRepo.GetEnabled();
        if (!connections.Any())
        {
            return Task.FromResult(HealthCheckResult.Notice(
                "NoArrConnections",
                "No *arr connections configured. Connect Sonarr, Radarr, or Lidarr in Settings > Connections to enable deep media enrichment and posters."));
        }

        return Task.FromResult(HealthCheckResult.Ok("NoArrConnections"));
    }
}
