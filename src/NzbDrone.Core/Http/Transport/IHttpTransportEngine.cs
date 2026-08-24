using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http.Transport;

public interface IHttpTransportEngine
{
    IHttpTransportProvider ActiveProvider { get; }
    string ActiveProviderId { get; }
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
