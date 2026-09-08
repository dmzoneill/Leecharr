// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoIndexersCheck : IHealthCheck
{
    private readonly IIndexerRepository indexerRepo;

    public NoIndexersCheck(IIndexerRepository indexerRepo)
    {
        this.indexerRepo = indexerRepo;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var indexers = this.indexerRepo.GetEnabled();
        if (!indexers.Any())
        {
            return Task.FromResult(HealthCheckResult.Notice(
                "NoIndexers",
                "No indexers configured. Add an indexer (Prowlarr, Torznab) in Settings > Indexers for integrated search and RSS sync."));
        }

        return Task.FromResult(HealthCheckResult.Ok("NoIndexers"));
    }
}
