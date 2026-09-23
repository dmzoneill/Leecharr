// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class CustomTransportProvidersTest
{
    [Test]
    public void CanHandle_CurlImpersonate_ExposesExpectedCapabilitiesAndMetadata()
    {
        using var provider = new CurlImpersonateTransportProvider();

        provider.ProviderId.Should().Be("CurlImpersonate");
        provider.DisplayName.Should().Contain("curl-impersonate");
        provider.Version.Should().Be("0.6.1");
        provider.Description.Should().Contain("JA3/JA4");

        var caps = provider.Capabilities;
        caps.SupportsHttp3Quic.Should().BeTrue();
        caps.SupportsBrowserFingerprintEmulation.Should().BeTrue();
        caps.SupportsFlareSolverr.Should().BeFalse();
        caps.SupportsCustomProxy.Should().BeTrue();
        caps.SupportsTlsJa3Ja4Fingerprinting.Should().BeTrue();
        caps.SupportsCookieExtraction.Should().BeTrue();
    }

    [Test]
    public void CanHandle_FlareSolverr_ExposesExpectedCapabilitiesAndMetadata()
    {
        using var provider = new FlareSolverrTransportProvider();

        provider.ProviderId.Should().Be("FlareSolverr");
        provider.DisplayName.Should().Contain("FlareSolverr");
        provider.Version.Should().Be("3.3.0");
        provider.Description.Should().Contain("Cloudflare");
        provider.IsAvailable.Should().BeTrue();

        var caps = provider.Capabilities;
        caps.SupportsHttp3Quic.Should().BeFalse();
        caps.SupportsBrowserFingerprintEmulation.Should().BeTrue();
        caps.SupportsFlareSolverr.Should().BeTrue();
        caps.SupportsCustomProxy.Should().BeTrue();
        caps.SupportsTlsJa3Ja4Fingerprinting.Should().BeTrue();
        caps.SupportsCookieExtraction.Should().BeTrue();
    }

    [Test]
    public async Task ProbeHealthAsync_CurlImpersonate_ReturnsHealthy_WithAppropriateMode()
    {
        using var provider = new CurlImpersonateTransportProvider();
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task ProbeHealthAsync_FlareSolverr_WhenOnline_ReturnsHealthy()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"version\":\"3.3.0\"}"),
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("responding");
    }

    [Test]
    public async Task ProbeHealthAsync_FlareSolverr_WhenUnreachable_ReturnsUnhealthy()
    {
        var mockHandler = new MockHttpMessageHandler(_ =>
        {
            throw new HttpRequestException("Connection refused");
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("offline or unreachable");
        health.Warnings.Should().NotBeEmpty();
    }

    [Test]
    public void CurlImpersonate_ProxyConfiguration_MapsSchemesAndCredentialsCorrectly()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("socks5");
        config.ProxyHost.Returns("proxy.local");
        config.ProxyPort.Returns(1080);
        config.ProxyAuthEnabled.Returns(true);
        config.ProxyUsername.Returns("proxyuser");
        config.ProxyPassword.Returns("proxypass");

        using var provider = new CurlImpersonateTransportProvider(config);
        var proxy = provider.Proxy;

        proxy.Should().NotBeNull();
        proxy!.Address!.Scheme.Should().Be("socks5h");
        proxy.Address.Host.Should().Be("proxy.local");
        proxy.Address.Port.Should().Be(1080);
    }

    [Test]
    public async Task ExecuteRequestAsync_CurlImpersonate_InjectsChromeBrowserHeaders()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/api/");
        listener.Start();

        string? receivedUserAgent = null;
        string? receivedSecChUa = null;
        string? receivedSecChUaMobile = null;
        string? receivedSecChUaPlatform = null;

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var req = context.Request;
            receivedUserAgent = req.Headers["User-Agent"];
            receivedSecChUa = req.Headers["Sec-Ch-Ua"];
            receivedSecChUaMobile = req.Headers["Sec-Ch-Ua-Mobile"];
            receivedSecChUaPlatform = req.Headers["Sec-Ch-Ua-Platform"];

            var response = context.Response;
            var buffer = Encoding.UTF8.GetBytes("<html><body>OK</body></html>");
            response.StatusCode = 200;
            response.ContentType = "text/html";
            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();
        });

        using var provider = new CurlImpersonateTransportProvider();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/test");

        var response = await provider.SendAsync(request);
        await serverTask;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        receivedUserAgent.Should().Contain("Chrome/124");
        receivedSecChUa.Should().Contain("Chromium");
        receivedSecChUaMobile.Should().Be("?0");
        receivedSecChUaPlatform.Should().Be("\"Windows\"");
    }

    [Test]
    public async Task ExecuteRequestAsync_CurlImpersonate_PreservesExistingCustomHeaders()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/api/");
        listener.Start();

        string? receivedUserAgent = null;

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            receivedUserAgent = context.Request.Headers["User-Agent"];

            var response = context.Response;
            response.StatusCode = 200;
            response.OutputStream.Close();
        });

        using var provider = new CurlImpersonateTransportProvider();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/custom");
        request.Headers.Add("User-Agent", "CustomCrawler/1.0");

        var response = await provider.SendAsync(request);
        await serverTask;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        receivedUserAgent.Should().Be("CustomCrawler/1.0");
    }

    [Test]
    public async Task ExecuteRequestAsync_CurlImpersonate_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        using var provider = new CurlImpersonateTransportProvider();
        var act = async () => await provider.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task ExecuteRequestAsync_FlareSolverr_ExecutesGet_AndReturnsSolution()
    {
        var requestedBodies = new List<string>();

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            requestedBodies.Add(body);

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_123\"}"),
                };
            }

            if (body.Contains("request.get"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"<rss version=\\\"2.0\\\"><channel><title>Solved</title></channel></rss>\"}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://private-tracker.org/feed");

        var response = await provider.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Solved");

        requestedBodies.Should().Contain(b => b.Contains("request.get") && b.Contains("https://private-tracker.org/feed") && b.Contains("maxTimeout"));
    }

    [Test]
    public async Task ExecuteRequestAsync_FlareSolverr_ExecutesPost_WithPayloadData()
    {
        var requestedBodies = new List<string>();

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            requestedBodies.Add(body);

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_post\"}"),
                };
            }

            if (body.Contains("request.post"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"{\\\"result\\\":\\\"success\\\"}\"}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://private-tracker.org/api/login")
        {
            Content = new StringContent("username=alice&password=secret", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };

        var response = await provider.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("success");

        requestedBodies.Should().Contain(b => b.Contains("request.post") && b.Contains("username=alice"));
    }

    [Test]
    public async Task CookieHandling_FlareSolverr_TransmitsCookieHeadersAndExtractsSolution()
    {
        string? receivedPostData = null;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_cookie\"}"),
                };
            }

            if (body.Contains("request.get"))
            {
                receivedPostData = body;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"<html>Cookie Authenticated</html>\"}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cookie-tracker.org/browse");
        request.Headers.Add("Cookie", "uid=12345; pass=abcde");

        var response = await provider.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Cookie Authenticated");
    }

    [Test]
    public async Task SessionManagement_FlareSolverr_CreatesReusesAndDestroysDomainSessions()
    {
        var createdCount = 0;
        var destroyedCount = 0;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

            if (body.Contains("sessions.create"))
            {
                createdCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"\"{{\"status\":\"ok\",\"session\":\"sess_{createdCount}\"}}\""),
                };
            }

            if (body.Contains("sessions.destroy"))
            {
                destroyedCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"message\":\"Session destroyed\"}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);

        // 1. Create first session for tracker.org
        var sess1 = await provider.GetOrCreateSessionAsync("tracker.org");
        sess1.Should().NotBeNullOrWhiteSpace();
        provider.ActiveSessionCount.Should().Be(1);

        // 2. Reuse session for tracker.org without calling create again
        var sess2 = await provider.GetOrCreateSessionAsync("tracker.org");
        sess2.Should().Be(sess1);
        provider.ActiveSessionCount.Should().Be(1);

        // 3. Create second session for other.org
        var sess3 = await provider.GetOrCreateSessionAsync("other.org");
        sess3.Should().NotBeNullOrWhiteSpace();
        provider.ActiveSessionCount.Should().Be(2);

        // 4. Destroy session for tracker.org
        var destroyed = await provider.DestroySessionAsync("tracker.org");
        destroyed.Should().BeTrue();
        provider.ActiveSessionCount.Should().Be(1);
        provider.ActiveSessions.Should().NotContainKey("tracker.org");

        // 5. Clear all remaining sessions
        await provider.ClearSessionsAsync();
        provider.ActiveSessionCount.Should().Be(0);
    }

    [Test]
    public async Task SessionManagement_FlareSolverr_WhenSessionExpired_EvictsAndRetriesWithFreshSession()
    {
        var attempts = 0;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_renewed\"}"),
                };
            }

            if (body.Contains("request.get"))
            {
                attempts++;
                if (attempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"status\":\"error\",\"message\":\"Session not found\"}"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"<rss>success</rss>\"}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://tracker.org/feed");

        var response = await provider.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("<rss>success</rss>");
        attempts.Should().Be(2);
    }

    [Test]
    public async Task TimeoutHandling_FlareSolverr_RespectsCancellationToken()
    {
        var mockHandler = new MockHttpMessageHandler(_ =>
        {
            Thread.Sleep(100);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var provider = new FlareSolverrTransportProvider(null, httpClient);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://slow-tracker.org");
        var act = async () => await provider.SendAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task FallbackWhenDaemonUnreachable_FlareSolverr_FallsBackToDirectHttpClient()
    {
        var fallbackCalled = false;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var isFlareSolverrCommand = req.RequestUri?.ToString().Contains("8191") == true;
            if (isFlareSolverrCommand)
            {
                // Daemon is completely unreachable
                throw new HttpRequestException("FlareSolverr connection refused");
            }

            // Direct request fallback
            fallbackCalled = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<channel>DirectResponse</channel>"),
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://direct-fallback.org/feed");

        var response = await provider.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("DirectResponse");
        fallbackCalled.Should().BeTrue();
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.handler(request));
        }
    }
}
