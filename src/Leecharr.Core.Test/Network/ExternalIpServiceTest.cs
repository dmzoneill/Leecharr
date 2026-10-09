// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;

namespace Leecharr.Core.Test.Network;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFunc;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
    {
        this.responseFunc = responseFunc;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(this.responseFunc(request));
    }
}

[TestFixture]
public class ExternalIpServiceTest
{
    private static void SetCache(ExternalIpService subject, string ip, DateTime lastFetch)
    {
        var fields = typeof(ExternalIpService).GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var ipField = fields.FirstOrDefault(f => f.Name.Contains("cachedIp", StringComparison.OrdinalIgnoreCase));
        var fetchField = fields.FirstOrDefault(f => f.Name.Contains("lastFetch", StringComparison.OrdinalIgnoreCase));
        ipField?.SetValue(subject, ip);
        fetchField?.SetValue(subject, lastFetch);
    }

    [Test]
    public void CachedIp_should_be_empty_by_default()
    {
        var subject = new ExternalIpService();
        Assert.That(subject.CachedIp, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task GetExternalIpAsync_should_return_cached_ip_when_cache_is_valid()
    {
        var subject = new ExternalIpService();
        SetCache(subject, "203.0.113.5", DateTime.UtcNow.AddMinutes(-5));

        var result = await subject.GetExternalIpAsync();
        Assert.That(result, Is.EqualTo("203.0.113.5"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_fetch_from_primary_endpoint_when_cache_empty()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri.Host.Contains("leecharr.net"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ip\": \"198.51.100.42\"}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var subject = new ExternalIpService(new HttpClient(handler));
        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("198.51.100.42"));
        Assert.That(subject.CachedIp, Is.EqualTo("198.51.100.42"));
    }

    [TestCase("{\"status\":\"success\",\"action\":\"inserted\",\"message\":\"Client entry inserted successfully.\",\"data\":{\"uuid\":\"f4acf080-fc83-4fc0-916c-603638964ed9\",\"ip\":\"161.230.102.224\",\"last_used\":1788187356}}", "161.230.102.224")]
    [TestCase("{\"ip\": \"146.70.231.15\"}", "146.70.231.15")]
    [TestCase("{\"ip_address\": \"146.70.231.15\"}", "146.70.231.15")]
    [TestCase("146.70.231.15", "146.70.231.15")]
    [TestCase("  146.70.231.15  \n", "146.70.231.15")]
    public void TryExtractIpFromResponse_should_parse_valid_formats(string input, string expected)
    {
        var success = ExternalIpService.TryExtractIpFromResponse(input, out var ip);

        Assert.That(success, Is.True);
        Assert.That(ip, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("invalid-ip")]
    [TestCase("{\"error\": \"not_found\"}")]
    public void TryExtractIpFromResponse_should_fail_for_invalid_formats(string input)
    {
        var success = ExternalIpService.TryExtractIpFromResponse(input, out var ip);

        Assert.That(success, Is.False);
        Assert.That(ip, Is.Empty);
    }

    private static WebProxy CreateHttpClientProxy(ExternalIpService subject)
    {
        var method = typeof(ExternalIpService).GetMethod("CreateHttpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        using var client = (HttpClient)method!.Invoke(subject, null)!;
        var handler = typeof(HttpMessageInvoker)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client);
        var socketsHandler = (System.Net.Http.SocketsHttpHandler)handler!;
        return (WebProxy)socketsHandler.Proxy!;
    }

    [Test]
    public void CreateHttpClient_WhenProxyAuthDisabled_DoesNotAttachCredentials()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("http");
        config.ProxyHost.Returns("proxy.example");
        config.ProxyPort.Returns(8080);
        config.ProxyAuthEnabled.Returns(false);
        config.ProxyUsername.Returns("saveduser");
        config.ProxyPassword.Returns("savedpass");

        var proxy = CreateHttpClientProxy(new ExternalIpService(config));

        proxy.Credentials.Should().BeNull();
    }

    [Test]
    public void CreateHttpClient_WhenProxyAuthEnabled_AttachesCredentials()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("http");
        config.ProxyHost.Returns("proxy.example");
        config.ProxyPort.Returns(8080);
        config.ProxyAuthEnabled.Returns(true);
        config.ProxyUsername.Returns("saveduser");
        config.ProxyPassword.Returns("savedpass");

        var proxy = CreateHttpClientProxy(new ExternalIpService(config));

        proxy.Credentials.Should().NotBeNull();
        var creds = proxy.Credentials!.GetCredential(new Uri("http://proxy.example:8080/"), "Basic");
        creds!.UserName.Should().Be("saveduser");
        creds.Password.Should().Be("savedpass");
    }

    [Test]
    public void GetExternalIpAsync_WhenProxyUriInvalid_ReleasesFetchLockForRetry()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks5");
        config.ProxyHost.Returns("2001:db8::1");
        config.ProxyPort.Returns(1080);
        config.InstanceUuid.Returns("test-uuid");

        var subject = new ExternalIpService(config);

        Assert.ThrowsAsync<UriFormatException>(() => subject.GetExternalIpAsync());
        Assert.ThrowsAsync<UriFormatException>(() => subject.GetExternalIpAsync());

        var lockField = typeof(ExternalIpService).GetField("fetchLock", BindingFlags.Instance | BindingFlags.NonPublic);
        var fetchLock = (SemaphoreSlim)lockField!.GetValue(subject)!;
        fetchLock.CurrentCount.Should().Be(1);
    }

    [Test]
    public void Constructor_AcceptsConfigAndBindingServices()
    {
        var configService = NSubstitute.Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        configService.ProxyType.Returns("socks5");
        configService.ProxyHost.Returns("127.0.0.1");
        configService.ProxyPort.Returns(1080);
        configService.BindInterface.Returns("tun0");

        var bindingService = NSubstitute.Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        var transportEngine = NSubstitute.Substitute.For<NzbDrone.Core.Http.Transport.IHttpTransportEngine>();

        var subject = new ExternalIpService(configService, bindingService, transportEngine);

        Assert.That(subject.CachedIp, Is.EqualTo(string.Empty));
    }
}
