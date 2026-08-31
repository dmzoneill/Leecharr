// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoIndexersCheck : IHealthCheck
{
    private readonly IIndexerRepository indexerRepo;

    public NoIndexersCheck(IIndexerRepository indexerRepo)
    {
        this.indexerRepo = indexerRepo;
    }

    public HealthCheckResult Check()
    {
        var indexers = this.indexerRepo.GetEnabled();
        if (!indexers.Any())
        {
            return HealthCheckResult.Notice(
                "NoIndexers",
                "No indexers configured. Add an indexer (Prowlarr, Torznab) in Settings > Indexers for integrated search and RSS sync.");
        }

        return HealthCheckResult.Ok("NoIndexers");
    }
}
