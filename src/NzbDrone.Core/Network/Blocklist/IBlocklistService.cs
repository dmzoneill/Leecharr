using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Blocklist;

public interface IBlocklistService
{
    bool IsIpBlocked(string ipAddress);
    Task<int> LoadRulesAsync(IEnumerable<string> rules);
    void ClearRules();
    int TotalRulesLoaded { get; }
}
