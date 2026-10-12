// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class TransportProviderTest
{
    [Test]
    public void CurlImpersonateTransportProvider_WithSocks5Proxy_UsesSocks5hScheme()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks5");
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(1080);
        config.ProxyAuthEnabled.Returns(true);
        config.ProxyUsername.Returns("testuser");
        config.ProxyPassword.Returns("testpass");

        using var provider = new CurlImpersonateTransportProvider(config);
        var proxy = provider.Proxy;

        proxy.Should().NotBeNull();
        proxy!.Address!.Scheme.Should().Be("socks5h");
        proxy.Address.ToString().Should().Be("socks5h://127.0.0.1:1080/");
        proxy.Credentials.Should().NotBeNull();
        var creds = proxy.Credentials.GetCredential(new Uri("socks5h://127.0.0.1:1080/"), "Basic");
        creds.Should().NotBeNull();
        creds!.UserName.Should().Be("testuser");
        creds.Password.Should().Be("testpass");
    }

    [Test]
    public async Task SocketsHttpHandlerProvider_WithSocks4Proxy_GuardsAgainstUnsupportedSchemeWithoutException()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks4");
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(1080);

        using var provider = new SocketsHttpHandlerProvider(config);

        provider.Handler.UseProxy.Should().BeFalse();
        provider.Handler.Proxy.Should().BeNull();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.Warnings.Should().Contain(w => w.Contains("SOCKS4"));
    }

    [Test]
    public void SocketsHttpHandlerProvider_WithSocks5Proxy_ConfiguresSocks5Scheme()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks5");
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(1080);

        using var provider = new SocketsHttpHandlerProvider(config);

        provider.Handler.UseProxy.Should().BeTrue();
        var proxy = provider.Handler.Proxy as WebProxy;
        proxy.Should().NotBeNull();
        proxy!.Address!.Scheme.Should().Be("socks5");
        proxy.Address.ToString().Should().Be("socks5://127.0.0.1:1080/");
    }

    [TestCase("::1", "socks5h://[::1]:1080/")]
    [TestCase("2001:db8::1", "socks5h://[2001:db8::1]:1080/")]
    public void CurlImpersonateTransportProvider_WithIpv6ProxyHost_BracketsAuthority(string proxyHost, string expectedUri)
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks5");
        config.ProxyHost.Returns(proxyHost);
        config.ProxyPort.Returns(1080);

        using var provider = new CurlImpersonateTransportProvider(config);
        provider.Proxy!.Address!.ToString().Should().Be(expectedUri);
    }

    [Test]
    public void SocketsHttpHandlerProvider_WithIpv6ProxyHost_BracketsAuthority()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("http");
        config.ProxyHost.Returns("2001:db8::1");
        config.ProxyPort.Returns(8080);

        using var provider = new SocketsHttpHandlerProvider(config);
        var proxy = provider.Handler.Proxy as WebProxy;
        proxy!.Address!.ToString().Should().Be("http://[2001:db8::1]:8080/");
    }
}
