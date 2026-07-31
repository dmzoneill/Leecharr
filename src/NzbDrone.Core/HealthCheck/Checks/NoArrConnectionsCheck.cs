using System.Linq;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoArrConnectionsCheck : IHealthCheck
{
    private readonly IArrConnectionRepository _arrRepo;

    public NoArrConnectionsCheck(IArrConnectionRepository arrRepo)
    {
        _arrRepo = arrRepo;
    }

    public HealthCheckResult Check()
    {
        var connections = _arrRepo.GetEnabled();
        if (!connections.Any())
        {
            return HealthCheckResult.Warning(
                "NoArrConnections",
                "No *arr connections configured. Connect Sonarr, Radarr, or Lidarr in Settings > Connections to enable deep media enrichment and posters.");
        }

        return HealthCheckResult.Ok("NoArrConnections");
    }
}
