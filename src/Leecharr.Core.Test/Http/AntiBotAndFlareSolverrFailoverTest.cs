// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class AntiBotAndFlareSolverrFailoverTest
{
    [Test]
    public void AntiBotChallengeDetector_DetectsCloudflareSignatures()
    {
        var cfBodies = new[]
        {
            "<html><head><title>Just a moment...</title></head><body>Checking your browser before accessing site.</body></html>",
            "<html><head><title>Attention Required! | Cloudflare</title></head><body>Please complete the security check</body></html>",
            "<html><head><title>Security Check | DDoS-GUARD</title></head><body>Please verify you are human</body></html>",
            "<script src=\"https://challenges.cloudflare.com/turnstile/v0/api.js\"></script><div class=\"cf-turnstile\"></div>",
            "<form id=\"challenge-form\" class=\"challenge-form\"><input name=\"_cf_chl_opt\" value=\"xyz\" /></form>",
            "<html><body><div id=\"cf-browser-verification\">Verifying...</div></body></html>",
            "<html><body><div>Protected by cloudflare-nginx</div></body></html>",
        };

        foreach (var body in cfBodies)
        {
            AntiBotChallengeDetector.IsChallenge(body).Should().BeTrue($"Signature in '{body}' should be detected");
            AntiBotChallengeDetector.IsChallenge(HttpStatusCode.Forbidden, body).Should().BeTrue();
            AntiBotChallengeDetector.IsChallenge(HttpStatusCode.ServiceUnavailable, body).Should().BeTrue();
        }
    }

    [Test]
    public async Task AntiBotChallengeDetector_WithHttpResponseMessage_DetectsChallenge()
    {
        var challengeResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<html><title>Just a moment...</title><body>Checking your browser</body></html>"),
        };

        var isChallenge = await AntiBotChallengeDetector.IsChallengeAsync(challengeResponse);
        isChallenge.Should().BeTrue();

        var normalResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>Normal</title></channel></rss>"),
        };

        var isNormalChallenge = await AntiBotChallengeDetector.IsChallengeAsync(normalResponse);
        isNormalChallenge.Should().BeFalse();
    }

    [Test]
    public async Task AntiBotChallengeDetector_WithCloudflareHeaders_DetectsChallenge()
    {
        var cfMitigatedResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("Access denied"),
        };
        cfMitigatedResponse.Headers.Add("cf-mitigated", "challenge");

        var isChallenge = await AntiBotChallengeDetector.IsChallengeAsync(cfMitigatedResponse);
        isChallenge.Should().BeTrue();
    }

    [Test]
    public async Task DynamicHttpTransportProxy_WhenCloudflareChallengeEncountered_TransparentlyFailsOverToFlareSolverr()
    {
        var socketsProvider = Substitute.For<IHttpTransportProvider>();
        socketsProvider.ProviderId.Returns("SocketsHttpHandler");
        socketsProvider.DisplayName.Returns("Sockets");
        socketsProvider.IsAvailable.Returns(true);
        socketsProvider.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true }));

        // SocketsHttpHandler returns Cloudflare 503 challenge
        socketsProvider.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("<html><title>Just a moment...</title><body>Turnstile challenge</body></html>"),
            }));

        var flareSolverr = Substitute.For<IHttpTransportProvider>();
        flareSolverr.ProviderId.Returns("FlareSolverr");
        flareSolverr.DisplayName.Returns("FlareSolverr");
        flareSolverr.IsAvailable.Returns(true);
        flareSolverr.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true }));

        // FlareSolverr returns resolved 200 OK
        flareSolverr.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<rss><channel><title>Solved Feed</title></channel></rss>"),
            }));

        var configService = Substitute.For<IConfigService>();
        configService.ActiveHttpTransportProvider.Returns("SocketsHttpHandler");
        var eventAggregator = Substitute.For<IEventAggregator>();

        using var proxy = new DynamicHttpTransportProxy(
            new[] { socketsProvider, flareSolverr },
            configService,
            eventAggregator);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://protected-indexer.local/api?t=caps");
        var response = await proxy.SendAsync(request);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Solved Feed");

        await socketsProvider.Received(1).SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>());
        await flareSolverr.Received(1).SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DynamicHttpTransportProxy_WhenFlareSolverrUnhealthy_ReturnsOriginalChallengeResponse()
    {
        var socketsProvider = Substitute.For<IHttpTransportProvider>();
        socketsProvider.ProviderId.Returns("SocketsHttpHandler");
        socketsProvider.DisplayName.Returns("Sockets");
        socketsProvider.IsAvailable.Returns(true);
        socketsProvider.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true }));

        socketsProvider.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<html><title>Attention Required! | Cloudflare</title></html>"),
            }));

        var flareSolverr = Substitute.For<IHttpTransportProvider>();
        flareSolverr.ProviderId.Returns("FlareSolverr");
        flareSolverr.DisplayName.Returns("FlareSolverr");
        flareSolverr.IsAvailable.Returns(true);
        flareSolverr.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = false, StatusMessage = "FlareSolverr offline" }));

        var configService = Substitute.For<IConfigService>();
        configService.ActiveHttpTransportProvider.Returns("SocketsHttpHandler");
        var eventAggregator = Substitute.For<IEventAggregator>();

        using var proxy = new DynamicHttpTransportProxy(
            new[] { socketsProvider, flareSolverr },
            configService,
            eventAggregator);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://protected-indexer.local/api");
        var response = await proxy.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await flareSolverr.DidNotReceive().SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FlareSolverrTransportProvider_SessionPooling_CreatesAndReusesSessionPerDomain()
    {
        var requestsReceived = new List<string>();

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            requestsReceived.Add(body);

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"message\":\"Session created\",\"session\":\"sess_tracker1\"}"),
                };
            }

            if (body.Contains("request.get"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"<rss>feed</rss>\"}}"),
                };
            }

            if (body.Contains("sessions.destroy"))
            {
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

        // First request to domain1 -> should trigger sessions.create and reuse session
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "https://tracker1.local/rss");
        var resp1 = await provider.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        provider.ActiveSessionCount.Should().Be(1);
        provider.ActiveSessions.Should().ContainKey("tracker1.local");

        // Second request to same domain1 -> should reuse existing session without creating a new one
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "https://tracker1.local/search?q=test");
        var resp2 = await provider.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        provider.ActiveSessionCount.Should().Be(1);

        // Third request to domain2 -> creates separate session
        using var req3 = new HttpRequestMessage(HttpMethod.Get, "https://tracker2.local/caps");
        var resp3 = await provider.SendAsync(req3);
        resp3.StatusCode.Should().Be(HttpStatusCode.OK);

        provider.ActiveSessionCount.Should().Be(2);
        provider.ActiveSessions.Should().ContainKey("tracker2.local");

        // Destroy session for domain1
        var destroyed = await provider.DestroySessionAsync("tracker1.local");
        destroyed.Should().BeTrue();
        provider.ActiveSessionCount.Should().Be(1);
        provider.ActiveSessions.Should().NotContainKey("tracker1.local");

        // Clear remaining sessions
        await provider.ClearSessionsAsync();
        provider.ActiveSessionCount.Should().Be(0);
    }

    [Test]
    public async Task FlareSolverrTransportProvider_WhenSessionExpired_RetriesWithNewSession()
    {
        var executionCount = 0;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

            if (body.Contains("sessions.create"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"message\":\"Session created\",\"session\":\"sess_fresh\"}"),
                };
            }

            if (body.Contains("request.get"))
            {
                executionCount++;
                if (executionCount == 1)
                {
                    // First attempt returns session not found error
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"status\":\"error\",\"message\":\"Session not found or expired\"}"),
                    };
                }

                // Second attempt with fresh session succeeds
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"solution\":{\"status\":200,\"response\":\"<xml>ok</xml>\"}}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(mockHandler);
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config, httpClient);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://tracker.local/feed");
        var resp = await provider.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await resp.Content.ReadAsStringAsync();
        content.Should().Be("<xml>ok</xml>");
        executionCount.Should().Be(2);
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
            return Task.FromResult(this.handler(request));
        }
    }
}
