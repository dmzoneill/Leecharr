// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.BitTorrent;

public interface ITorrentEngineManager
{
    ITorrentEngine ActiveEngine { get; }

    string ActiveEngineId { get; }

    IEnumerable<ITorrentEngine> GetEngines();

    ITorrentEngine GetEngine(string engineId);

    Task<EngineHealthCheckResult> ProbeEngineAsync(string engineId);

    Task<EngineSwitchResult> SwitchEngineAsync(string targetEngineId, bool preserveTransfers = true);
}
