using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Binding;

public interface INetworkBindingManager
{
    INetworkBindingProvider ActiveProvider { get; }
    string ActiveProviderId { get; }
    IEnumerable<INetworkBindingProvider> GetProviders();
    INetworkBindingProvider GetProvider(string providerId);
    Task<NetworkBindingHealthCheckResult> ProbeProviderAsync(string providerId);
    Task<NetworkBindingSwitchResult> SwitchProviderAsync(string targetProviderId);
}
