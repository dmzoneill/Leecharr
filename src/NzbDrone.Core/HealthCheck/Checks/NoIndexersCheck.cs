using System.Linq;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoIndexersCheck : IHealthCheck
{
    private readonly IIndexerRepository _indexerRepo;

    public NoIndexersCheck(IIndexerRepository indexerRepo)
    {
        _indexerRepo = indexerRepo;
    }

    public HealthCheckResult Check()
    {
        var indexers = _indexerRepo.GetEnabled();
        if (!indexers.Any())
        {
            return HealthCheckResult.Notice(
                "NoIndexers",
                "No indexers configured. Add an indexer (Prowlarr, Torznab) in Settings > Indexers for integrated search and RSS sync.");
        }

        return HealthCheckResult.Ok("NoIndexers");
    }
}
