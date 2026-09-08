// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Http;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class SafeHttpClientServiceTest
{
    private SafeHttpClientService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new SafeHttpClientService();
    }

    [TearDown]
    public void TearDown()
    {
        this.service?.Dispose();
    }

    #region SSRF URL Validation Tests

    [TestCase("http://127.0.0.1/test.torrent")]
    [TestCase("http://127.0.0.2:8080/test.torrent")]
    [TestCase("http://127.255.255.254/")]
    [TestCase("https://127.0.0.1/")]
    public void ValidateUrl_WhenLoopbackIp_ThrowsSecurityException(string url)
    {
        var act = () => this.service.ValidateUrl(url);
        act.Should().Throw<SecurityException>()
            .WithMessage("*SSRF blocked*");
    }

    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://169.254.1.1/internal")]
    [TestCase("https://169.254.169.254/")]
    public void ValidateUrl_WhenCloudMetadataIp_ThrowsSecurityException(string url)
    {
        var act = () => this.service.ValidateUrl(url);
        act.Should().Throw<SecurityException>()
            .WithMessage("*SSRF blocked*");
    }

    [TestCase("http://localhost/test.torrent")]
    [TestCase("http://localhost:5000/")]
    [TestCase("https://localhost/")]
    public void ValidateUrl_WhenLocalhost_ThrowsSecurityException(string url)
    {
        var act = () => this.service.ValidateUrl(url);
        act.Should().Throw<SecurityException>()
            .WithMessage("*SSRF blocked*");
    }

    [TestCase("http://[::1]/test.torrent")]
    [TestCase("http://[::1]:8080/")]
    public void ValidateUrl_WhenIpv6Loopback_ThrowsSecurityException(string url)
    {
        var act = () => this.service.ValidateUrl(url);
        act.Should().Throw<SecurityException>()
            .WithMessage("*SSRF blocked*");
    }

    [TestCase("http://0.0.0.0/test.torrent")]
    [TestCase("http://0.0.0.1/")]
    public void ValidateUrl_WhenUnspecifiedIp_ThrowsSecurityException(string url)
    {
        var act = () => this.service.ValidateUrl(url);
        act.Should().Throw<SecurityException>()
            .WithMessage("*SSRF blocked*");
    }

    [TestCase("file:///etc/passwd")]
    [TestCase("gopher://127.0.0.1:70/")]
    [TestCase("ftp://example.com/test.torrent")]
    public void ValidateUrl_WhenNonHttpScheme_ThrowsSecurityException(string url)
    {
        var act = () => this.service.ValidateUrl(url);
        act.Should().Throw<SecurityException>()
            .WithMessage("*Unsupported URI scheme*");
    }

    #endregion

    #region IsBlockedIp Unit Tests

    [Test]
    public void IsBlockedIp_DetectsProhibitedIpAddressesCorrectly()
    {
        this.service.IsBlockedIp(IPAddress.Parse("127.0.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("127.1.2.3")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("169.254.169.254")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("169.254.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("0.0.0.0")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("255.255.255.255")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.IPv6Loopback).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.IPv6Any).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("fe80::1")).Should().BeTrue();

        // RFC1918 Private IPv4 and CGNAT
        this.service.IsBlockedIp(IPAddress.Parse("10.0.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("172.16.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("172.31.255.255")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("192.168.1.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("100.64.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("100.127.255.255")).Should().BeTrue();

        // IPv6 ULA
        this.service.IsBlockedIp(IPAddress.Parse("fc00::1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("fd00::1")).Should().BeTrue();

        // IPv4-mapped IPv6 bypass attempts
        this.service.IsBlockedIp(IPAddress.Parse("::ffff:127.0.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("::ffff:169.254.169.254")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("::ffff:192.168.1.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("::ffff:10.0.0.1")).Should().BeTrue();

        // Public IPs should not be blocked
        this.service.IsBlockedIp(IPAddress.Parse("8.8.8.8")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("1.1.1.1")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("93.184.216.34")).Should().BeFalse();
    }

    #endregion

    #region SSRF Allowlist and LAN Support Tests

    [Test]
    public void AllowPrivateNetworkRequests_WhenTrue_PermitsLanAndLoopbackIps()
    {
        this.service.AllowPrivateNetworkRequests = true;

        this.service.IsBlockedIp(IPAddress.Parse("127.0.0.1")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("192.168.1.100")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("10.1.2.3")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("172.16.5.10")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.IPv6Loopback).Should().BeFalse();

        // Cloud metadata remains blocked
        this.service.IsBlockedIp(IPAddress.Parse("169.254.169.254")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("0.0.0.0")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("255.255.255.255")).Should().BeTrue();

        var act = () => this.service.ValidateUrl("http://localhost:9696/api/v1/indexer");
        act.Should().NotThrow();

        var actIp = () => this.service.ValidateUrl("http://192.168.1.50:9696/api/v1/indexer");
        actIp.Should().NotThrow();
    }

    [Test]
    public void AllowedSsrfHostnames_WhenConfigured_PermitsSpecificAndWildcardHosts()
    {
        this.service.AllowedSsrfHostnames = "prowlarr, *.local, sonarr.lan";

        this.service.IsAllowedHost("prowlarr").Should().BeTrue();
        this.service.IsAllowedHost("PROWLARR").Should().BeTrue();
        this.service.IsAllowedHost("indexer.local").Should().BeTrue();
        this.service.IsAllowedHost("sonarr.lan").Should().BeTrue();
        this.service.IsAllowedHost("unauthorized.com").Should().BeFalse();

        var actProwlarr = () => this.service.ValidateUrl("http://prowlarr:9696/api/v1/indexer");
        actProwlarr.Should().NotThrow();

        var actLocal = () => this.service.ValidateUrl("http://my-indexer.local:8080/torznab");
        actLocal.Should().NotThrow();
    }

    [Test]
    public void AllowedSsrfSubnets_WhenConfigured_PermitsCidrRangesAndSpecificIps()
    {
        this.service.AllowedSsrfSubnets = "192.168.0.0/16, 10.50.0.0/24, 172.20.0.5";

        this.service.IsBlockedIp(IPAddress.Parse("192.168.1.1")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("192.168.254.10")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("10.50.0.15")).Should().BeFalse();
        this.service.IsBlockedIp(IPAddress.Parse("172.20.0.5")).Should().BeFalse();

        // Other private subnets not in allowlist remain blocked
        this.service.IsBlockedIp(IPAddress.Parse("10.10.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("172.16.0.1")).Should().BeTrue();
        this.service.IsBlockedIp(IPAddress.Parse("127.0.0.1")).Should().BeTrue();

        var act = () => this.service.ValidateUrl("http://192.168.1.1:9696/api/v1/indexer");
        act.Should().NotThrow();
    }

    #endregion

    #region Payload Response Size Limit and Timeout Tests

    [Test]
    public async Task DownloadBytesAsync_WhenContentLengthExceedsLimit_ThrowsInvalidOperationExceptionImmediately()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[10]),
            };
            response.Content.Headers.ContentLength = 20 * 1024 * 1024; // 20 MB advertised
            return response;
        });

        using var safeClient = new SafeHttpClientService(handler);

        var act = async () => await safeClient.DownloadBytesAsync("https://93.184.216.34/huge.torrent", maxSizeBytes: 10 * 1024 * 1024);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds maximum allowed size*");
    }

    [Test]
    public async Task DownloadBytesAsync_WhenStreamExceedsLimitWithoutContentLength_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[500]),
            };
            response.Content.Headers.ContentLength = null;
            return response;
        });

        using var safeClient = new SafeHttpClientService(handler);

        var act = async () => await safeClient.DownloadBytesAsync("https://93.184.216.34/chunked.torrent", maxSizeBytes: 100);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeded maximum allowed limit*");
    }

    [Test]
    public async Task DownloadBytesAsync_WhenValidResponseWithinLimit_ReturnsBytesSuccessfully()
    {
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes),
        });

        using var safeClient = new SafeHttpClientService(handler);

        var result = await safeClient.DownloadBytesAsync("https://93.184.216.34/valid.torrent", maxSizeBytes: 1000);

        result.Should().BeEquivalentTo(expectedBytes);
    }

    [Test]
    public async Task DownloadStringAsync_ReturnsStringSuccessfully()
    {
        var expectedString = "<caps><server/></caps>";
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(expectedString),
        });

        using var safeClient = new SafeHttpClientService(handler);

        var result = await safeClient.DownloadStringAsync("https://93.184.216.34/api?t=caps", timeout: TimeSpan.FromSeconds(5));

        result.Should().Be(expectedString);
    }

    [TestCase("http://127.0.0.1/ssrf.torrent")]
    [TestCase("http://169.254.169.254/meta-data")]
    public async Task DownloadBytesAsync_WhenUrlIsProhibited_ThrowsSecurityException(string prohibitedUrl)
    {
        var act = async () => await this.service.DownloadBytesAsync(prohibitedUrl);

        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*SSRF blocked*");
    }

    #endregion

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.handler(request));
        }
    }
}
