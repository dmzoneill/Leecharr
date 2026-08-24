using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Blocklist;

public interface IBlocklistManager
{
    string ActiveProviderId { get; }
    IBlocklistProvider ActiveProvider { get; }
    IEnumerable<IBlocklistProvider> GetProviders();
    IBlocklistProvider GetProvider(string providerId);
    Task<BlocklistHealthResult> ProbeProviderAsync(string providerId);
    Task<bool> SwitchProviderAsync(string providerId);
}
