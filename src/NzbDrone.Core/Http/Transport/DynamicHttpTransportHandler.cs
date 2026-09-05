// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http.Transport;

public class DynamicHttpTransportHandler : HttpMessageHandler
{
    private readonly IHttpTransportEngine engine;

    public DynamicHttpTransportHandler(IHttpTransportEngine engine)
    {
        this.engine = engine;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await this.engine.SendAsync(request, cancellationToken);
    }
}
