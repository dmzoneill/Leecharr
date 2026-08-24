using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public interface IMediaMetadataManager
{
    IMediaMetadataProvider ActiveProvider { get; }
    string ActiveProviderId { get; }
    IEnumerable<IMediaMetadataProvider> GetProviders();
    IMediaMetadataProvider GetProvider(string providerId);
    Task<MediaMetadataHealthCheckResult> ProbeProviderAsync(string providerId);
    Task<MediaMetadataSwitchResult> SwitchProviderAsync(string targetProviderId);
}
