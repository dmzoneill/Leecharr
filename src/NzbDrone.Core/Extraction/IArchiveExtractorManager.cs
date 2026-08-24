using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Extraction;

public interface IArchiveExtractorManager
{
    IArchiveExtractorProvider ActiveProvider { get; }
    string ActiveProviderId { get; }
    IEnumerable<IArchiveExtractorProvider> GetProviders();
    IArchiveExtractorProvider GetProvider(string providerId);
    Task<ExtractorHealthCheckResult> ProbeProviderAsync(string providerId, CancellationToken cancellationToken = default);
    Task<ExtractorSwitchResult> SwitchProviderAsync(string targetProviderId, CancellationToken cancellationToken = default);
}
