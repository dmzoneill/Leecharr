// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

    [TestCase("socks4", "socks4", "socks4://127.0.0.1:1080/")]
    [TestCase("socks4a", "socks4a", "socks4a://127.0.0.1:1080/")]
    [TestCase("socks5", "socks5", "socks5://127.0.0.1:1080/")]
    public void SocketsHttpHandlerProvider_WithSocksProxy_UsesNativeScheme(string proxyType, string expectedScheme, string expectedUri)
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns(proxyType);
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(1080);

        using var provider = new SocketsHttpHandlerProvider(config);

        provider.Handler.UseProxy.Should().BeTrue();
        var proxy = provider.Handler.Proxy as WebProxy;
        proxy.Should().NotBeNull();
        proxy!.Address!.Scheme.Should().Be(expectedScheme);
        proxy.Address.ToString().Should().Be(expectedUri);
    }

    [Test]
    public async Task SocketsHttpHandlerProvider_WithSocks4Proxy_TunnelsInsteadOfConnectingDirectly()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var header = new byte[8];
            await ReadExactAsync(stream, header);

            int next;
            while ((next = stream.ReadByte()) > 0)
            {
            }

            if (next < 0)
            {
                throw new InvalidOperationException("SOCKS4 client closed before the userid terminator.");
            }

            await stream.WriteAsync(new byte[] { 0x00, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            var requestBuffer = new byte[2048];
            var received = new StringBuilder();
            while (received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal) == false)
            {
                var read = await stream.ReadAsync(requestBuffer);
                if (read == 0)
                {
                    break;
                }

                received.Append(Encoding.ASCII.GetString(requestBuffer, 0, read));
            }

            var responseBytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
            await stream.WriteAsync(responseBytes);
            return (Version: header[0], Command: header[1], Request: received.ToString());
        });

        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks4");
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(port);
        config.AllowPrivateNetworkRequests.Returns(true);

        try
        {
            using var provider = new SocketsHttpHandlerProvider(config);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://1.2.3.4/health");
            using var response = await provider.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var handshake = await serverTask.WaitAsync(timeout.Token);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Should().Be("ok");
            handshake.Version.Should().Be(0x04);
            handshake.Command.Should().Be(0x01);
            handshake.Request.Should().Contain("GET /health");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task SocketsHttpHandlerProvider_WithSocks4Proxy_HealthCheckDoesNotClaimSchemeIsUnsupported()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks4");
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(1080);

        using var provider = new SocketsHttpHandlerProvider(config);
        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeTrue();
        health.Warnings.Should().NotContain(w => w.Contains("not supported", StringComparison.OrdinalIgnoreCase));
    }

    [TestCase("socks5", "socks5h://127.0.0.1:1080/")]
    [TestCase("socks4", "socks4://127.0.0.1:1080/")]
    [TestCase("socks4a", "socks4a://127.0.0.1:1080/")]
    public void CurlImpersonateTransportProvider_PassesProxySchemeOnCurlCommandLine(string proxyType, string expectedProxy)
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns(proxyType);
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(1080);

        using var provider = new CurlImpersonateTransportProvider(config);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://indexer.example/rss");
        string requestBodyFile = null;
        var startInfo = provider.BuildCurlStartInfo(request, "curl-impersonate", "headers.txt", "body.bin", ref requestBodyFile);
        var args = startInfo.ArgumentList.ToList();
        var proxyIndex = args.IndexOf("--proxy");

        proxyIndex.Should().BeGreaterThanOrEqualTo(0);
        args[proxyIndex + 1].Should().Be(expectedProxy);
    }

    [TestCase("socks5", "socks5")]
    [TestCase("socks4", "socks4")]
    [TestCase("socks4a", "socks4a")]
    public void FlareSolverrFallback_WithSocksProxy_UsesHandlerSupportedScheme(string proxyType, string expectedScheme)
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns(proxyType);
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(1080);

        using var provider = new FlareSolverrTransportProvider(config);
        provider.FallbackHandler.Should().NotBeNull();
        provider.FallbackHandler!.UseProxy.Should().BeTrue();
        provider.SolverHandler!.UseProxy.Should().BeFalse();

        var proxy = provider.FallbackHandler.Proxy as WebProxy;
        proxy.Should().NotBeNull();
        proxy!.Address!.Scheme.Should().Be(expectedScheme);
        proxy.Address.Scheme.Should().NotBe("socks5h");
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read == 0)
            {
                throw new InvalidOperationException("SOCKS client closed before the handshake header was complete.");
            }

            offset += read;
        }
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
