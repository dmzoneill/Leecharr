// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Blocklist;

public interface IBlocklistService
{
    bool IsIpBlocked(string ipAddress);

    Task<int> LoadRulesAsync(IEnumerable<string> rules);

    Task<int> AddRulesAsync(IEnumerable<string> rules) => Task.FromResult(0);

    void ClearRules();

    int TotalRulesLoaded { get; }
}
