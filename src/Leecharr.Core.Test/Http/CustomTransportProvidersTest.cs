// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
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
public class CustomTransportProvidersTest
{
    [Test]
    public void CanHandle_CurlImpersonate_ExposesExpectedCapabilitiesAndMetadata()
    {
        using var provider = CurlImpersonateTestHelper.CreateProvider();

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
        using var provider = CurlImpersonateTestHelper.CreateProvider();
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task ProbeHealthAsync_CurlImpersonate_WhenBinaryMissing_ReturnsUnhealthy()
    {
        using var provider = new CurlImpersonateTransportProvider();
        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("not detected");
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
    public async Task ProbeHealthAsync_FlareSolverr_WhenEndpointIsDown_ReturnsUnhealthy()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("offline or unreachable");
    }

    [Test]
    public void FlareSolverr_ProxyConfiguration_BypassesOutboundProxyForSolverClient()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("http");
        config.ProxyHost.Returns("proxy.local");
        config.ProxyPort.Returns(8080);
        config.ProxyAuthEnabled.Returns(true);
        config.ProxyUsername.Returns("proxyuser");
        config.ProxyPassword.Returns("proxypass");

        using var provider = new FlareSolverrTransportProvider(config);

        provider.SolverHandler.Should().NotBeNull();
        provider.SolverHandler!.UseProxy.Should().BeFalse();

        provider.FallbackHandler.Should().NotBeNull();
        provider.FallbackHandler!.UseProxy.Should().BeTrue();
        var proxy = provider.FallbackHandler.Proxy as WebProxy;
        proxy.Should().NotBeNull();
        proxy!.Address!.Host.Should().Be("proxy.local");
        proxy.Address.Port.Should().Be(8080);
    }

    [Test]
    public void RefreshOutboundProxy_WhenProxyHostChanges_UpdatesSocketsHandlerProxy()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("http");
        config.ProxyHost.Returns("proxy-a.local");
        config.ProxyPort.Returns(8080);
        config.ProxyAuthEnabled.Returns(false);

        using var provider = new SocketsHttpHandlerProvider(config);
        var initialProxy = provider.Handler.Proxy as WebProxy;
        initialProxy.Should().NotBeNull();
        initialProxy!.Address!.Host.Should().Be("proxy-a.local");

        config.ProxyHost.Returns("proxy-b.local");
        provider.RefreshOutboundProxy();

        var updatedProxy = provider.Handler.Proxy as WebProxy;
        updatedProxy!.Address!.Host.Should().Be("proxy-b.local");
    }

    [Test]
    public void RefreshOutboundProxy_WhenProxyDisabled_ClearsSocketsHandlerProxy()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("http");
        config.ProxyHost.Returns("proxy.local");
        config.ProxyPort.Returns(8080);
        config.ProxyAuthEnabled.Returns(false);

        using var provider = new SocketsHttpHandlerProvider(config);
        provider.Handler.UseProxy.Should().BeTrue();

        config.ProxyType.Returns("none");
        provider.RefreshOutboundProxy();

        provider.Handler.UseProxy.Should().BeFalse();
        provider.Handler.Proxy.Should().BeNull();
    }

    [Test]
    public void RefreshOutboundProxy_FlareSolverr_UpdatesFallbackHandlerOnly()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyType.Returns("http");
        config.ProxyHost.Returns("proxy-a.local");
        config.ProxyPort.Returns(8080);
        config.ProxyAuthEnabled.Returns(false);

        using var provider = new FlareSolverrTransportProvider(config);
        provider.SolverHandler!.UseProxy.Should().BeFalse();
        (provider.FallbackHandler!.Proxy as WebProxy)!.Address!.Host.Should().Be("proxy-a.local");

        config.ProxyHost.Returns("proxy-b.local");
        provider.RefreshOutboundProxy();

        provider.SolverHandler!.UseProxy.Should().BeFalse();
        (provider.FallbackHandler!.Proxy as WebProxy)!.Address!.Host.Should().Be("proxy-b.local");
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

        using var provider = CurlImpersonateTestHelper.CreateProvider(config);
        var proxy = provider.Proxy;

        proxy.Should().NotBeNull();
        proxy!.Address!.Scheme.Should().Be("socks5h");
        proxy.Address.Host.Should().Be("proxy.local");
        proxy.Address.Port.Should().Be(1080);
    }

    [Test]
    public async Task ExecuteRequestAsync_CurlImpersonate_InjectsChromeBrowserHeaders()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string requestText = null!;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer);
            requestText = Encoding.ASCII.GetString(buffer, 0, read);
            var responseBytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
            await stream.WriteAsync(responseBytes);
        });

        using var provider = CurlImpersonateTestHelper.CreateProvider();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/test");

        var response = await provider.SendAsync(request);
        await serverTask;
        listener.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requestText.Should().Contain("Chrome/124");
        requestText.Should().Contain("Chromium");
        requestText.Should().Contain("Sec-Ch-Ua-Mobile: ?0");
        requestText.Should().Contain("Sec-Ch-Ua-Platform: \"Windows\"");
    }

    [Test]
    public async Task ExecuteRequestAsync_CurlImpersonate_PreservesExistingCustomHeaders()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/api/");
        listener.Start();

        string receivedUserAgent = null!;

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            receivedUserAgent = context.Request.Headers["User-Agent"];

            var response = context.Response;
            response.StatusCode = 200;
            response.OutputStream.Close();
        });

        using var provider = CurlImpersonateTestHelper.CreateProvider();
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
        using var provider = CurlImpersonateTestHelper.CreateProvider();
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
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/rss+xml");
    }

    [Test]
    public async Task ExecuteRequestAsync_FlareSolverr_PreservesSolutionHeadersAndBinaryBody()
    {
        var torrentBytes = Encoding.UTF8.GetBytes("d8:announce35:http://tracker.example/announceee");
        var torrentBase64 = Convert.ToBase64String(torrentBytes);

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_bin\"}"),
                };
            }

            if (body.Contains("request.get"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"status\":\"ok\",\"solution\":{{\"status\":200,\"responseEncoding\":\"base64\",\"response\":\"{torrentBase64}\",\"headers\":{{\"content-type\":\"application/x-bittorrent\",\"x-test\":\"present\"}}}}}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://private-tracker.org/download?id=1");

        var response = await provider.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("x-test").Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/x-bittorrent");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(torrentBytes);
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
        string receivedPostData = null!;

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
        receivedPostData.Should().Contain("uid");
        receivedPostData.Should().Contain("12345");
        receivedPostData.Should().Contain("pass");
        receivedPostData.Should().Contain("abcde");
    }

    [Test]
    public async Task SendAsync_FlareSolverr_ReusesSolutionUserAgentAndClearanceCookies()
    {
        var requestBodies = new List<string>();
        const string solvedUserAgent = "SolvedAgent/1.0";

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            requestBodies.Add(body);

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_clearance\"}"),
                };
            }

            if (body.Contains("request.get"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":\"ok\",\"solution\":{" +
                        "\"status\":200,\"response\":\"<html>solved</html>\"," +
                        "\"userAgent\":\"" + solvedUserAgent + "\"," +
                        "\"cookies\":[" +
                        "{\"name\":\"cf_clearance\",\"value\":\"clearance-token\"}," +
                        "{\"name\":\"__cf_bm\",\"value\":\"bm-token\"}," +
                        "{\"name\":\"other\",\"value\":\"ignore-me\"}]}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var first = new HttpRequestMessage(HttpMethod.Get, "https://tracker.example/challenge");
        var firstResponse = await provider.SendAsync(first);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = new HttpRequestMessage(HttpMethod.Get, "https://tracker.example/download.torrent");
        second.Headers.TryAddWithoutValidation("User-Agent", "Leecharr/1.0");
        second.Headers.TryAddWithoutValidation("Cookie", "cf_clearance=stale; uid=1");

        await provider.SendAsync(second);

        second.Headers.GetValues("User-Agent").Should().ContainSingle().Which.Should().Be(solvedUserAgent);
        var cookie = string.Join("; ", second.Headers.GetValues("Cookie"));
        cookie.Should().Contain("cf_clearance=clearance-token");
        cookie.Should().Contain("__cf_bm=bm-token");
        cookie.Should().Contain("uid=1");
        cookie.Should().NotContain("stale");
        cookie.Should().NotContain("ignore-me");

        var solvedRequests = requestBodies.Where(body => body.Contains("request.get")).ToList();
        solvedRequests.Should().HaveCount(2);
        solvedRequests[1].Should().Contain(solvedUserAgent);
        solvedRequests[1].Should().Contain("cf_clearance");
        solvedRequests[1].Should().Contain("clearance-token");
        solvedRequests[1].Should().Contain("__cf_bm");
        solvedRequests[1].Should().Contain("bm-token");
        solvedRequests[1].Should().NotContain("stale");
        solvedRequests[1].Should().NotContain("ignore-me");
    }

    [Test]
    public async Task SendAsync_FlareSolverr_ForwardsHeadersAndContentWithoutForcingHtml()
    {
        var requestBodies = new List<string>();

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            requestBodies.Add(body);

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_forward\"}"),
                };
            }

            if (body.Contains("request.post"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"{\\\"ok\\\":true}\"}}"),
                };
            }

            if (body.Contains("request.get"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"<?xml version=\\\"1.0\\\"?><rss><channel></channel></rss>\"}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var post = new HttpRequestMessage(HttpMethod.Post, "https://tracker.example/api")
        {
            Content = new StringContent("username=alice&password=secret", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        post.Headers.TryAddWithoutValidation("Cookie", "uid=12345");
        post.Headers.TryAddWithoutValidation("Authorization", "Bearer token-1");
        post.Headers.TryAddWithoutValidation("Referer", "https://tracker.example/login");

        var postResponse = await provider.SendAsync(post);
        var postType = postResponse.Content.Headers.ContentType?.MediaType;
        postType.Should().Be("application/json");
        postType.Should().NotBe("text/html");

        using var get = new HttpRequestMessage(HttpMethod.Get, "https://tracker.example/feed");
        var getResponse = await provider.SendAsync(get);
        var getType = getResponse.Content.Headers.ContentType?.MediaType;
        getType.Should().Be("application/rss+xml");
        getType.Should().NotBe("text/html");

        var forwarded = requestBodies.Single(body => body.Contains("request.post"));
        forwarded.Should().Contain("username=alice");
        forwarded.Should().Contain("password=secret");
        forwarded.Should().Contain("uid");
        forwarded.Should().Contain("12345");
        forwarded.Should().Contain("Authorization");
        forwarded.Should().Contain("Bearer token-1");
        forwarded.Should().Contain("Referer");
        forwarded.Should().Contain("https://tracker.example/login");
    }

    [Test]
    public void BuildNewSessionId_WhenDomainNamesShareLongPrefix_ProducesDistinctIdsWithFullGuidSuffix()
    {
        var first = FlareSolverrTransportProvider.BuildNewSessionId("tracker-private-announce.co.uk");
        var second = FlareSolverrTransportProvider.BuildNewSessionId("tracker-private-announce.com");

        first.Should().NotBe(second);
        first.Length.Should().BeGreaterThan(32);
        second.Length.Should().BeGreaterThan(32);

        foreach (var sessionId in new[] { first, second })
        {
            var guidSuffix = sessionId[(sessionId.LastIndexOf('_') + 1)..];
            guidSuffix.Length.Should().Be(32);
            guidSuffix.Should().MatchRegex("^[0-9a-f]{32}$");
        }
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
                    Content = new StringContent($"{{\"status\":\"ok\",\"session\":\"sess_{createdCount}\"}}"),
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
        destroyedCount.Should().Be(2);
    }

    [Test]
    public async Task SessionManagement_FlareSolverr_ConcurrentGetOrCreateSession_CreatesOnlyOncePerDomain()
    {
        var createdCount = 0;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

            if (body.Contains("sessions.create"))
            {
                Interlocked.Increment(ref createdCount);
                Thread.Sleep(50);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_concurrent\"}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetOrCreateSessionAsync("tracker.org"))
            .ToArray();
        var sessions = await Task.WhenAll(tasks);

        sessions.Should().OnlyContain(s => s == "sess_concurrent");
        createdCount.Should().Be(1);
        provider.ActiveSessionCount.Should().Be(1);
    }

    [Test]
    public async Task SessionManagement_FlareSolverr_DisposeDestroysActiveSessionsOnSolver()
    {
        var destroyedCount = 0;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_dispose\"}"),
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

        var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        var provider = new FlareSolverrTransportProvider(config, httpClient);
        await provider.GetOrCreateSessionAsync("tracker.org");
        await provider.GetOrCreateSessionAsync("other.org");
        provider.ActiveSessionCount.Should().Be(2);

        provider.Dispose();

        destroyedCount.Should().Be(2);
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

    [Test]
    public async Task ExecuteRequestAsync_FlareSolverr_UnsupportedMethods_UseDirectHttpClient()
    {
        var solverBodies = new List<string>();
        var directMethods = new List<string>();

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            var isFlareSolverrCommand = req.RequestUri?.ToString().Contains("8191") == true;
            if (isFlareSolverrCommand)
            {
                solverBodies.Add(body);

                if (body.Contains("sessions.create"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"status\":\"ok\",\"session\":\"sess_methods\"}"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"unused\"}}"),
                };
            }

            directMethods.Add(req.Method.Method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("direct"),
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);

        using (var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "https://tracker.org/item/1"))
        {
            var deleteResponse = await provider.SendAsync(deleteRequest);
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var putRequest = new HttpRequestMessage(HttpMethod.Put, "https://tracker.org/item/1"))
        {
            putRequest.Content = new StringContent("{}");
            var putResponse = await provider.SendAsync(putRequest);
            putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        directMethods.Should().Equal("DELETE", "PUT");
        solverBodies.Should().NotContain(b => b.Contains("request.get") && b.Contains("/item/1"));
    }

    [Test]
    public async Task ExecuteRequestAsync_FlareSolverr_WhenPostFails_DoesNotRetryDirectly()
    {
        var directPostAttempts = 0;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            var isFlareSolverrCommand = req.RequestUri?.ToString().Contains("8191") == true;
            if (isFlareSolverrCommand)
            {
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
                        Content = new StringContent("{\"status\":\"error\",\"message\":\"solver rejected post\"}"),
                    };
                }
            }

            if (req.Method == HttpMethod.Post)
            {
                directPostAttempts++;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("direct"),
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://tracker.org/login")
        {
            Content = new StringContent("username=alice&password=secret"),
        };

        var response = await provider.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        directPostAttempts.Should().Be(0);
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
