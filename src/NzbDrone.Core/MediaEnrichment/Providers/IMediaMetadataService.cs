using System.Threading.Tasks;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public interface IMediaMetadataService
{
    IMediaMetadataProvider ActiveProvider { get; }
    string ActiveProviderId { get; }
    Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null);
}
