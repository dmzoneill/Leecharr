// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class WebhookDispatcherComprehensiveIntegrationTests : IntegrationTestBase
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
    public void WebhookDispatcher_IsValidTargetUrl_ValidatesUrlsAndSsrf()
    {
        // 1. Valid URLs
        WebhookDispatcher.IsValidTargetUrl("https://discord.com/api/webhooks/123/abc").Should().BeTrue();
        WebhookDispatcher.IsValidTargetUrl("http://hooks.slack.com/services/123/456").Should().BeTrue();
        WebhookDispatcher.IsValidTargetUrl("https://api.telegram.org/bot123/sendMessage").Should().BeTrue();

        // 2. Invalid schemes and malformed URLs
        WebhookDispatcher.IsValidTargetUrl("").Should().BeFalse();
        WebhookDispatcher.IsValidTargetUrl("   ").Should().BeFalse();
        WebhookDispatcher.IsValidTargetUrl("ftp://files.example.com").Should().BeFalse();
        WebhookDispatcher.IsValidTargetUrl("file:///etc/passwd").Should().BeFalse();
        WebhookDispatcher.IsValidTargetUrl("not_a_url").Should().BeFalse();

        // 3. Localhost & private IPs when allowLoopback is false
        WebhookDispatcher.IsValidTargetUrl("http://localhost:8080/hook", allowLoopback: false).Should().BeFalse();
        WebhookDispatcher.IsValidTargetUrl("http://127.0.0.1:8080/hook", allowLoopback: false).Should().BeFalse();
        WebhookDispatcher.IsValidTargetUrl("http://169.254.169.254/metadata", allowLoopback: false).Should().BeFalse();

        // 4. Localhost allowed when allowLoopback is true
        WebhookDispatcher.IsValidTargetUrl("http://localhost:8080/hook", allowLoopback: true).Should().BeTrue();
        WebhookDispatcher.IsValidTargetUrl("http://127.0.0.1:8080/hook", allowLoopback: true).Should().BeTrue();
    }

    [Test]
    public async Task WebhookDispatcher_DispatchAsync_SendsPayloadWithHeaders()
    {
        var receivedPayload = string.Empty;
        var receivedAuth = string.Empty;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Content != null)
            {
                receivedPayload = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }

            if (req.Headers.TryGetValues("Authorization", out var authVals))
            {
                receivedAuth = string.Join(" ", authVals);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json"),
            };
        });

        using var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var customHeadersJson = "{\"Authorization\": \"Bearer test_token_xyz\", \"X-Custom-Header\": \"LeecharrIntegrationTest\"}";

        var result = await dispatcher.DispatchAsync(
            "http://127.0.0.1:8080/webhook",
            new { @event = "TorrentComplete", id = 42 },
            customHeadersJson);

        result.Should().BeTrue();
        receivedPayload.Should().Contain("TorrentComplete");
        receivedAuth.Should().Contain("Bearer test_token_xyz");
    }
}
