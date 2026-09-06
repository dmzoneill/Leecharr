// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoArrConnectionsCheck : IHealthCheck
{
    private readonly IArrConnectionRepository arrRepo;

    public NoArrConnectionsCheck(IArrConnectionRepository arrRepo)
    {
        this.arrRepo = arrRepo;
    }

    public HealthCheckResult Check()
    {
        var connections = this.arrRepo.GetEnabled();
        if (!connections.Any())
        {
            return HealthCheckResult.Notice(
                "NoArrConnections",
                "No *arr connections configured. Connect Sonarr, Radarr, or Lidarr in Settings > Connections to enable deep media enrichment and posters.");
        }

        return HealthCheckResult.Ok("NoArrConnections");
    }
}
