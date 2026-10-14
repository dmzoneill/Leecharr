// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Leecharr.Core.Test.BitTorrent;

internal static class DaemonRpcTestDoubles
{
    public static HttpClient CreateClient()
    {
        return new HttpClient(new PermissiveDaemonHandler());
    }

    private sealed class PermissiveDaemonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string body = "{\"result\":\"success\",\"arguments\":{\"torrent-added\":{\"id\":1},\"version\":\"4.0.6\"}}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
