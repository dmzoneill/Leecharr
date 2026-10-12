// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Http.Transport;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class CurlImpersonateTransportProviderTest
{
    [Test]
    public void TryParseLastHttpResponse_UsesFinalRedirectBlock()
    {
        var dump = "HTTP/1.1 302 Found\r\nLocation: http://example.com/final\r\n\r\nHTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n";

        var parsed = CurlImpersonateTransportProvider.TryParseLastHttpResponse(dump, out var status, out var headers);

        parsed.Should().BeTrue();
        status.Should().Be(HttpStatusCode.OK);
        headers["Content-Type"].Should().Be("text/html");
    }

    [Test]
    public void BuildHttpResponseMessage_MapsContentType()
    {
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://example.com");
        var headerDump = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n\r\n";

        using var response = CurlImpersonateTransportProvider.BuildHttpResponseMessage(request, headerDump, System.Text.Encoding.UTF8.GetBytes("ok"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }
}
