using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Ai;

public interface IAiManager
{
    string ActiveProviderId { get; }
    IAiEngineProvider ActiveProvider { get; }
    IEnumerable<IAiEngineProvider> GetProviders();
    IAiEngineProvider GetProvider(string providerId);
    Task<AiHealthResult> ProbeProviderAsync(string providerId);
    Task<bool> SwitchProviderAsync(string providerId);
}
