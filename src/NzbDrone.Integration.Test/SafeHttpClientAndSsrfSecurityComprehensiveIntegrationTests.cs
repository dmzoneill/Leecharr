// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Http;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SafeHttpClientAndSsrfSecurityComprehensiveIntegrationTests : IntegrationTestBase
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.handler(request));
        }
    }

    [Test]
    public void SafeHttpClient_ValidateUri_BlocksSsrfAndPrivateIps()
    {
        var service = new SafeHttpClientService();
        service.AllowPrivateNetworkRequests = false;

        // 1. Unsupported schemes
        Action actFtp = () => service.ValidateUri(new Uri("ftp://example.com/file.txt"));
        actFtp.Should().Throw<SecurityException>().WithMessage("*Unsupported URI scheme*");

        Action actFile = () => service.ValidateUri(new Uri("file:///etc/passwd"));
        actFile.Should().Throw<SecurityException>().WithMessage("*Unsupported URI scheme*");

        // 2. Localhost
        Action actLocalhost = () => service.ValidateUri(new Uri("http://localhost:8080/api"));
        actLocalhost.Should().Throw<SecurityException>().WithMessage("*localhost*prohibited*");

        // 3. Loopback IP
        Action actLoopback = () => service.ValidateUri(new Uri("http://127.0.0.1:5000/"));
        actLoopback.Should().Throw<SecurityException>().WithMessage("*SSRF blocked*");

        // 4. Cloud Metadata IP (169.254.169.254)
        Action actMetadata = () => service.ValidateUri(new Uri("http://169.254.169.254/latest/meta-data/"));
        actMetadata.Should().Throw<SecurityException>().WithMessage("*SSRF blocked*");

        // 5. Private IP range 10.x and 192.168.x
        Action actPrivate10 = () => service.ValidateUri(new Uri("http://10.0.0.1/admin"));
        actPrivate10.Should().Throw<SecurityException>().WithMessage("*SSRF blocked*");

        Action actPrivate192 = () => service.ValidateUri(new Uri("http://192.168.1.1/router"));
        actPrivate192.Should().Throw<SecurityException>().WithMessage("*SSRF blocked*");

        // 6. Whitelisting via AllowedSsrfHostnames
        service.AllowedSsrfHostnames = "internal.example.org,allowed.lan";
        service.IsAllowedHost("internal.example.org").Should().BeTrue();
        service.IsAllowedHost("other.example.org").Should().BeFalse();

        // 7. Whitelisting via AllowedSsrfSubnets
        service.AllowedSsrfSubnets = "192.168.100.0/24";
        service.IsAllowedIp(IPAddress.Parse("192.168.100.42")).Should().BeTrue();
        service.IsAllowedIp(IPAddress.Parse("192.168.200.42")).Should().BeFalse();
    }

    [Test]
    public async Task SafeHttpClient_DownloadOperations_EnforcesSizeAndHeaders()
    {
        var responseContent = "Sample HTTP Payload for Testing SafeHttpClientService";
        var responseBytes = Encoding.UTF8.GetBytes(responseContent);

        var handler = new MockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes),
            };
            resp.Content.Headers.Add("Content-Type", "text/plain");
            return resp;
        });

        using var service = new SafeHttpClientService(handler);
        service.AllowPrivateNetworkRequests = true; // allow loopback mock URI

        // 1. DownloadStringAsync
        var content = await service.DownloadStringAsync("http://127.0.0.1:8080/test");
        content.Should().Be(responseContent);

        // 2. DownloadBytesAsync
        var bytes = await service.DownloadBytesAsync("http://127.0.0.1:8080/test");
        bytes.Should().Equal(responseBytes);

        // 3. Max size limit enforcement
        Func<Task> actExceed = async () => await service.DownloadBytesAsync("http://127.0.0.1:8080/test", maxSizeBytes: 10);
        await actExceed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds maximum allowed size*");
    }
}
