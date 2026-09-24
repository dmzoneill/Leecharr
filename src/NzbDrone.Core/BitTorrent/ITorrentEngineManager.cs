// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.BitTorrent;

public interface ITorrentEngineManager
{
    ITorrentEngine ActiveEngine { get; }

    string ActiveEngineId { get; }

    string ActiveEngineVersion { get; }

    IEnumerable<ITorrentEngine> GetEngines();

    ITorrentEngine GetEngine(string engineId);

    Task<EngineHealthCheckResult> ProbeEngineAsync(string engineId);

    Task<EngineHealthCheckResult> ProbeEngineAsync(string engineId, string version);

    Task<EngineSwitchResult> SwitchEngineAsync(string targetEngineId, bool preserveTransfers = true);

    Task<EngineSwitchResult> SwitchEngineAsync(string targetEngineId, string targetVersion, bool preserveTransfers = true);

    Task<EngineSwitchResult> SwitchVersionAsync(string targetVersion, bool preserveTransfers = true);
}
