using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http.Transport;

public interface IHttpTransportProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    string Version { get; }
    string Description { get; }
    bool IsAvailable { get; }
    HttpTransportCapabilities Capabilities { get; }
    Task<HttpTransportHealthCheckResult> ProbeHealthAsync();
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
