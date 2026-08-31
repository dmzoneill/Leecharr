// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Blocklist;

public interface IBlocklistProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Version { get; }

    bool IsAvailable { get; }

    BlocklistCapabilities Capabilities { get; }

    int RuleCount { get; }

    Task<BlocklistHealthResult> ProbeHealthAsync();

    bool IsIpBlocked(string ipAddress);

    Task<int> LoadRulesAsync(IEnumerable<string> rules);

    void ClearRules();
}
