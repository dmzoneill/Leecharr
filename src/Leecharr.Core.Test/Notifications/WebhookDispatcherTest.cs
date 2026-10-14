// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using Polly.Retry;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class WebhookDispatcherTest
{
    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> SentRequests { get; } = new();

        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        public Func<HttpRequestMessage, Exception> ExceptionFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.SentRequests.Add(request);

            if (this.ExceptionFactory != null)
            {
                var ex = this.ExceptionFactory(request);
                if (ex != null)
                {
                    throw ex;
                }
            }

            return Task.FromResult(this.ResponseFactory(request));
        }
    }

    private TestHttpMessageHandler handler = null!;
    private HttpClient httpClient = null!;
    private AsyncRetryPolicy<HttpResponseMessage> fastRetryPolicy = null!;
    private WebhookDispatcher dispatcher = null!;

    [SetUp]
    public void SetUp()
    {
        this.handler = new TestHttpMessageHandler();
        this.httpClient = new HttpClient(this.handler);
        this.fastRetryPolicy = WebhookDispatcher.CreateRetryPolicy(
            retryCount: 3,
            sleepDurationProvider: _ => TimeSpan.FromMilliseconds(1));

        this.dispatcher = new WebhookDispatcher(this.httpClient, this.fastRetryPolicy);
    }

    [TearDown]
    public void TearDown()
    {
        this.httpClient.Dispose();
        this.handler.Dispose();
    }

    [Test]
    public async Task DispatchAsync_WhenUrlEmpty_ReturnsFalse()
    {
        var result = await this.dispatcher.DispatchAsync(string.Empty, new { eventType = "Test" });
        result.Should().BeFalse();
        this.handler.SentRequests.Should().BeEmpty();
    }

    [Test]
    public async Task DispatchAsync_SuccessfulRequest_SendsPostWithJsonPayloadAndReturnsTrue()
    {
        var payload = new { EventType = "OnGrab", TorrentName = "Ubuntu.iso" };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", payload);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://example.com/webhook"));
        request.Content.Should().NotBeNull();
        request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");

        var contentBody = await request.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(contentBody);
        doc.RootElement.GetProperty("eventType").GetString().Should().Be("OnGrab");
        doc.RootElement.GetProperty("torrentName").GetString().Should().Be("Ubuntu.iso");
    }

    [Test]
    public async Task DispatchAsync_WithCustomHeaders_AddsHeadersToRequest()
    {
        var customHeaders = "{\"X-Auth-Token\":\"secret123\",\"X-Custom-Header\":\"val456\"}";

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, customHeaders);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Headers.GetValues("X-Auth-Token").Should().ContainSingle().Which.Should().Be("secret123");
        request.Headers.GetValues("X-Custom-Header").Should().ContainSingle().Which.Should().Be("val456");
    }

    [Test]
    public async Task DispatchAsync_WithHttpMethodPut_SendsPutRequest()
    {
        var payload = new { EventType = "OnGrab", TorrentName = "Ubuntu.iso" };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", payload, null, HttpMethod.Put);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri.Should().Be(new Uri("https://example.com/webhook"));
    }

    [Test]
    public async Task DispatchAsync_WithBasicAuthHeader_AttachesAuthorizationHeader()
    {
        var headers = "{\"Authorization\":\"Basic YWRtaW46cGFzc3dvcmQ=\"}";

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, headers);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Basic YWRtaW46cGFzc3dvcmQ=");
    }

    [Test]
    public async Task DispatchAsync_WhenServerError500_RetriesAndSucceeds()
    {
        var attempts = 0;
        this.handler.ResponseFactory = req =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(3);
    }

    [Test]
    public async Task DispatchAsync_WhenAllRetriesFail500_ReturnsFalse()
    {
        this.handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(4); // initial + 3 retries
    }

    [Test]
    public async Task DispatchAsync_WhenHttpRequestException_RetriesAndSucceeds()
    {
        var attempts = 0;
        this.handler.ExceptionFactory = req =>
        {
            attempts++;
            return attempts < 2 ? new HttpRequestException("Connection reset") : null;
        };
        this.handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(2);
    }

    [Test]
    public async Task DispatchAsync_WhenClientError400_DoesNotRetryAndReturnsFalse()
    {
        this.handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.BadRequest);

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(1); // 4xx does not retry
    }

    [Test]
    public async Task DispatchAsync_WhenTelegramCannotParseEntities_RetriesWithoutParseModeOrBackslashes()
    {
        var attempts = 0;
        this.handler.ResponseFactory = _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"ok\":false,\"description\":\"Bad Request: can't parse entities\"}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var payload = new Dictionary<string, object>
        {
            ["text"] = "*Leecharr \\[OnGrab\\]*\n*Movie.Name.\\(2024\\)*",
            ["parse_mode"] = "Markdown",
            ["chat_id"] = "12345",
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", payload);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(2);

        var retryBody = await this.handler.SentRequests[1].Content!.ReadAsStringAsync();
        retryBody.Should().NotContain("parse_mode");
        retryBody.Should().NotContain("parseMode");
        retryBody.Should().Contain("Movie.Name.(2024)");
        retryBody.Should().Contain("[OnGrab]");
        retryBody.Should().NotContain(@"\(");
        retryBody.Should().NotContain(@"\[");
        retryBody.Should().Contain("12345");
    }

    [Test]
    public void RemoveParseMode_DropsParseModeAndUnescapesText()
    {
        var payload = new Dictionary<string, object>
        {
            ["text"] = @"Movie.Name.\(2024\) and C:\\Path",
            ["parse_mode"] = "Markdown",
            ["chat_id"] = "12345",
        };

        var result = WebhookDispatcher.RemoveParseMode(payload).Should().BeOfType<Dictionary<string, object>>().Subject;

        result.Should().NotContainKey("parse_mode");
        result["chat_id"].Should().Be("12345");
        result["text"].Should().Be(@"Movie.Name.(2024) and C:\Path");
    }

    [Test]
    public void RemoveParseMode_UnescapesStringDictionaryAndJsonText()
    {
        var stringPayload = new Dictionary<string, string>
        {
            ["text"] = @"Name\_2024",
            ["parseMode"] = "Markdown",
        };

        var stringResult = WebhookDispatcher.RemoveParseMode(stringPayload).Should().BeOfType<Dictionary<string, string>>().Subject;
        stringResult.Should().NotContainKey("parseMode");
        stringResult["text"].Should().Be("Name_2024");

        var jsonPayload = "{\"text\":\"Movie.Name.\\\\(2024\\\\)\",\"parse_mode\":\"Markdown\",\"chat_id\":\"9\"}";
        var jsonResult = WebhookDispatcher.RemoveParseMode(jsonPayload).Should().BeOfType<Dictionary<string, object>>().Subject;
        jsonResult.Should().NotContainKey("parse_mode");
        jsonResult["text"].Should().Be("Movie.Name.(2024)");
        ((JsonElement)jsonResult["chat_id"]).GetString().Should().Be("9");
    }

    [Test]
    public async Task DispatchAsync_WhenCustomHeadersJsonInvalid_StillDispatchesPayloadSuccessfully()
    {
        var invalidHeadersJson = "{ not-a-valid-json }";

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, invalidHeadersJson);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);
    }

    [Test]
    public async Task DispatchAsync_WhenTooManyRequests429_RetriesAndSucceeds()
    {
        var attempts = 0;
        this.handler.ResponseFactory = req =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await this.dispatcher.DispatchAsync("https://api.telegram.org/bot123/sendMessage", new { text = "Test" });

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(3);
    }

    [Test]
    public async Task DispatchAsync_WhenAllRetriesFail429_ReturnsFalse()
    {
        this.handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var result = await this.dispatcher.DispatchAsync("https://api.pushover.net/1/messages.json", new { message = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(4); // initial + 3 retries
    }

    [Test]
    public async Task DispatchAsync_WhenTimeoutException_RetriesAndSucceeds()
    {
        var attempts = 0;
        this.handler.ExceptionFactory = req =>
        {
            attempts++;
            return attempts < 3 ? new TimeoutException("The HTTP request timed out") : null;
        };
        this.handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(3);
    }

    [Test]
    public async Task DispatchAsync_WhenTaskCanceledExceptionDueToTimeout_RetriesAndSucceeds()
    {
        var attempts = 0;
        this.handler.ExceptionFactory = req =>
        {
            attempts++;
            return attempts < 2
                ? new TaskCanceledException("The operation was canceled due to HttpClient.Timeout elapsing", new TimeoutException())
                : null;
        };
        this.handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(2);
    }

    [Test]
    public async Task DispatchAsync_WhenAllRetriesFailTimeout_ReturnsFalse()
    {
        this.handler.ExceptionFactory = _ => new TimeoutException("Persistent timeout");

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(4); // initial + 3 retries
    }

    [Test]
    public async Task DispatchAsync_WhenAllRetriesFailTaskCanceledTimeout_ReturnsFalse()
    {
        this.handler.ExceptionFactory = _ => new TaskCanceledException("Timeout canceled", new TimeoutException());

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(4); // initial + 3 retries
    }

    [Test]
    public async Task DispatchAsync_WhenCanceledByCallerToken_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, cancellationToken: cts.Token);

        result.Should().BeFalse();
    }

    [Test]
    public async Task DispatchAsync_WithKeyValueHeadersFormat_AddsHeadersToRequest()
    {
        var customHeaders = "Authorization: Bearer my-key-123\r\nX-Tracking-Id: track-789";

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, customHeaders);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer my-key-123");
        request.Headers.GetValues("X-Tracking-Id").Should().ContainSingle().Which.Should().Be("track-789");
    }

    [Test]
    public async Task DispatchAsync_WithEqualsKeyValueHeadersFormat_AddsHeadersToRequest()
    {
        var customHeaders = "X-Api-Key=secret-value\nX-Service-Id=service-456";

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, customHeaders);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Headers.GetValues("X-Api-Key").Should().ContainSingle().Which.Should().Be("secret-value");
        request.Headers.GetValues("X-Service-Id").Should().ContainSingle().Which.Should().Be("service-456");
    }

    [Test]
    public async Task DispatchAsync_WithWhitespaceInHeaders_TrimsWhitespaceAndAddsHeaders()
    {
        var customHeaders = "  {  \"  X-Spaced-Header  \"  :  \"   trimmed-value   \"  }  ";

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, customHeaders);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Headers.GetValues("X-Spaced-Header").Should().ContainSingle().Which.Should().Be("trimmed-value");
    }

    [Test]
    public async Task DispatchAsync_WithContentHeader_AddsToContentHeaders()
    {
        var customHeaders = "{\"Content-Language\":\"en-US\"}";

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, customHeaders);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Content!.Headers.GetValues("Content-Language").Should().ContainSingle().Which.Should().Be("en-US");
    }

    [Test]
    public async Task DispatchAsync_WhenPushoverTargetAndDictionaryPayload_SendsFormUrlEncodedContent()
    {
        var payload = new Dictionary<string, object>
        {
            ["token"] = "app-token-123",
            ["user"] = "user-key-456",
            ["title"] = "Leecharr: OnGrab",
            ["message"] = "Ubuntu.iso (tv) - Downloading",
        };

        var result = await this.dispatcher.DispatchAsync("https://api.pushover.net/1/messages.json", payload);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://api.pushover.net/1/messages.json"));
        request.Content.Should().NotBeNull();
        request.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");

        var body = await request.Content.ReadAsStringAsync();
        body.Should().Contain("token=app-token-123");
        body.Should().Contain("user=user-key-456");
        body.Should().Contain("title=Leecharr%3A+OnGrab");
        body.Should().Contain("message=Ubuntu.iso+%28tv%29+-+Downloading");
    }

    [Test]
    public async Task DispatchAsync_WhenPushoverTargetAndStringDictionaryPayload_SendsFormUrlEncodedContent()
    {
        var payload = new Dictionary<string, string>
        {
            ["token"] = "app-token-789",
            ["user"] = "user-key-999",
            ["message"] = "Test Message",
        };

        var result = await this.dispatcher.DispatchAsync("https://api.pushover.net/1/messages.json", payload);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        request.Content.Should().NotBeNull();
        request.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");

        var body = await request.Content.ReadAsStringAsync();
        body.Should().Contain("token=app-token-789");
        body.Should().Contain("user=user-key-999");
        body.Should().Contain("message=Test+Message");
    }

    [Test]
    public void RedactTargetUrlForLogging_RedactsTelegramDiscordAndSlackWebhookSecrets()
    {
        const string telegramToken = "1234567890:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
        var telegramUrl = $"https://api.telegram.org/bot{telegramToken}/sendMessage";
        var redactedTelegram = WebhookDispatcher.RedactTargetUrlForLogging(telegramUrl);

        redactedTelegram.Should().Contain("api.telegram.org");
        redactedTelegram.Should().Contain("sendMessage");
        redactedTelegram.Should().NotContain(telegramToken);

        const string discordToken = "abcdefghijklmnopqrstuvwxyz1234567890ABCDEFGHIJKLMNOPQRST";
        var discordUrl = $"https://discord.com/api/webhooks/1234567890123456789/{discordToken}";
        var redactedDiscord = WebhookDispatcher.RedactTargetUrlForLogging(discordUrl);

        redactedDiscord.Should().Contain("discord.com");
        redactedDiscord.Should().Contain("/api/webhooks/");
        redactedDiscord.Should().NotContain(discordToken);

        const string slackSecret = "abcdefghijklmnopqrstuvwxyz1234567890";
        var slackUrl = $"https://hooks.slack.com/services/T00000000/B00000000/{slackSecret}";
        var redactedSlack = WebhookDispatcher.RedactTargetUrlForLogging(slackUrl);

        redactedSlack.Should().Contain("hooks.slack.com");
        redactedSlack.Should().NotContain(slackSecret);
    }

    [Test]
    public void RedactHeadersForLogging_RedactsSecretsAndRetainsKeysOnly()
    {
        var rawJson = "{\"Authorization\": \"Bearer sk-secret-token-12345\", \"X-Api-Key\": \"super-secret-key-999\"}";
        var redacted = WebhookDispatcher.RedactHeadersForLogging(rawJson);

        redacted.Should().Contain("Authorization");
        redacted.Should().Contain("X-Api-Key");
        redacted.Should().NotContain("sk-secret-token-12345");
        redacted.Should().NotContain("super-secret-key-999");

        var rawLines = "Authorization: Bearer secret\nX-Token=my-token";
        var redactedLines = WebhookDispatcher.RedactHeadersForLogging(rawLines);

        redactedLines.Should().Contain("Authorization");
        redactedLines.Should().Contain("X-Token");
        redactedLines.Should().NotContain("secret");
        redactedLines.Should().NotContain("my-token");
    }

    [TestCase("http://127.0.0.1/webhook")]
    [TestCase("http://127.0.1.1:8080/test")]
    [TestCase("http://localhost:5000/webhook")]
    [TestCase("http://sub.localhost/api")]
    [TestCase("http://[::1]/webhook")]
    [TestCase("http://169.254.169.254/latest/meta-data/")]
    [TestCase("http://169.254.1.2/webhook")]
    [TestCase("http://0.0.0.0/test")]
    [TestCase("http://255.255.255.255/test")]
    [TestCase("http://224.0.0.1/test")]
    [TestCase("http://instance-data/test")]
    [TestCase("http://metadata.google.internal/test")]
    [TestCase("http://10.0.0.1/webhook")]
    [TestCase("http://172.16.0.1/webhook")]
    [TestCase("http://172.31.255.255/webhook")]
    [TestCase("http://192.168.1.1/webhook")]
    [TestCase("http://100.64.0.1/webhook")]
    [TestCase("http://[fd00::1]/webhook")]
    [TestCase("http://[fc00::1]/webhook")]
    [TestCase("http://[fe80::1]/webhook")]
    public void IsValidTargetUrl_WhenProhibitedSsrfTarget_ReturnsFalse(string url)
    {
        WebhookDispatcher.IsValidTargetUrl(url, allowLoopback: false).Should().BeFalse();
    }

    [TestCase("http://127.0.0.1:8080/webhook")]
    [TestCase("http://localhost:5000/webhook")]
    [TestCase("http://[::1]:8080/webhook")]
    [TestCase("http://10.0.0.1:8080/webhook")]
    [TestCase("http://192.168.1.1:8080/webhook")]
    [TestCase("http://[fd00::1]:8080/webhook")]
    public void IsValidTargetUrl_WhenAllowLoopbackIsTrue_AllowsLoopback(string url)
    {
        WebhookDispatcher.IsValidTargetUrl(url, allowLoopback: true).Should().BeTrue();
    }

    [TestCase("http://127.0.0.1:8080/webhook")]
    [TestCase("http://localhost:5000/webhook")]
    [TestCase("http://[::1]:8080/webhook")]
    [TestCase("http://10.0.0.1:8080/webhook")]
    [TestCase("http://192.168.1.1:8080/webhook")]
    [TestCase("http://172.16.0.1:8080/webhook")]
    [TestCase("http://[fd00::1]:8080/webhook")]
    public void IsValidTargetUrl_WhenAllowPrivateNetworkRequestsIsTrue_AllowsLanTargets(string url)
    {
        WebhookDispatcher.IsValidTargetUrl(url, allowPrivateNetworkRequests: true).Should().BeTrue();
    }

    [TestCase("http://169.254.169.254/latest/meta-data/")]
    [TestCase("http://169.254.1.2/webhook")]
    [TestCase("http://instance-data/test")]
    [TestCase("http://metadata.google.internal/test")]
    [TestCase("http://0.0.0.0/test")]
    [TestCase("http://[fe80::1]/webhook")]
    public void IsValidTargetUrl_WhenAllowPrivateNetworkRequestsIsTrue_StillBlocksMetadataAndLinkLocal(string url)
    {
        WebhookDispatcher.IsValidTargetUrl(url, allowPrivateNetworkRequests: true).Should().BeFalse();
    }

    [Test]
    public async Task DispatchAsync_WhenAllowPrivateNetworkRequests_AllowsLanTarget()
    {
        var dispatcher = new WebhookDispatcher(this.httpClient, this.fastRetryPolicy, allowPrivateNetworkRequests: true);

        var result = await dispatcher.DispatchAsync("http://192.168.1.50/webhook", new { eventType = "Test" });

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);
    }

    [Test]
    public async Task DispatchAsync_WhenTargetIsBlockedBySsrfProtection_ReturnsFalseWithoutSendingHttp()
    {
        var result = await this.dispatcher.DispatchAsync("http://169.254.169.254/latest/meta-data/", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().BeEmpty();
    }

    [Test]
    public async Task DispatchAsync_WhenHostnameDoesNotResolve_ReturnsFalseWithoutSendingHttp()
    {
        var result = await this.dispatcher.DispatchAsync("http://leecharr-webhook.invalid/hook", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().BeEmpty();
    }

    [Test]
    public void IsValidTargetUrl_WhenDnsLookupThrows_ReturnsFalse()
    {
        WebhookDispatcher.IsValidTargetUrl(
            "https://rebinding.example/hook",
            allowLoopback: false,
            allowPrivateNetworkRequests: false,
            resolveHost: _ => throw new SocketException((int)SocketError.HostNotFound))
            .Should().BeFalse();
    }

    [Test]
    public void IsValidTargetUrl_WhenDnsReturnsNoAddresses_ReturnsFalse()
    {
        WebhookDispatcher.IsValidTargetUrl(
            "https://rebinding.example/hook",
            allowLoopback: false,
            allowPrivateNetworkRequests: false,
            resolveHost: _ => [])
            .Should().BeFalse();
    }

    [TestCase("169.254.169.254")]
    [TestCase("127.0.0.1")]
    [TestCase("10.1.2.3")]
    [TestCase("fd00::1")]
    public void IsValidTargetUrl_WhenDnsReturnsBlockedAddress_ReturnsFalse(string address)
    {
        WebhookDispatcher.IsValidTargetUrl(
            "https://rebinding.example/hook",
            allowLoopback: false,
            allowPrivateNetworkRequests: false,
            resolveHost: _ => [IPAddress.Parse(address)])
            .Should().BeFalse();
    }

    [Test]
    public void IsValidTargetUrl_WhenAnyResolvedAddressIsBlocked_ReturnsFalse()
    {
        WebhookDispatcher.IsValidTargetUrl(
            "https://rebinding.example/hook",
            allowLoopback: false,
            allowPrivateNetworkRequests: false,
            resolveHost: _ => [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.1.1.1")])
            .Should().BeFalse();
    }

    [Test]
    public void IsValidTargetUrl_WhenDnsReturnsPublicAddress_ReturnsTrue()
    {
        WebhookDispatcher.IsValidTargetUrl(
            "https://rebinding.example/hook",
            allowLoopback: false,
            allowPrivateNetworkRequests: false,
            resolveHost: _ => [IPAddress.Parse("93.184.216.34")])
            .Should().BeTrue();
    }

    [Test]
    public void EnsureConnectable_WhenLookupIsEmptyOrBlocked_Throws()
    {
        var empty = () => WebhookDispatcher.EnsureConnectable("rebinding.example", [], false, false);
        empty.Should().Throw<SecurityException>();

        var blocked = () => WebhookDispatcher.EnsureConnectable(
            "rebinding.example",
            [IPAddress.Parse("169.254.169.254")],
            false,
            false);
        blocked.Should().Throw<SecurityException>();
    }

    [Test]
    public async Task PinnedHandler_WhenSecondLookupIsPrivate_RejectsBeforeConnect()
    {
        using var handler = WebhookDispatcher.CreatePinnedSocketsHttpHandler(
            allowLoopback: false,
            allowPrivateNetworkRequests: false,
            resolveHostAsync: (_, _) => Task.FromResult(new[] { IPAddress.Parse("169.254.169.254") }));
        using var client = new HttpClient(handler, disposeHandler: false);

        var act = async () => await client.GetAsync("http://rebinding.example/hook");

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        thrown.Which.InnerException.Should().BeOfType<SecurityException>();
    }

    [Test]
    public async Task PinnedHandler_ConnectCallbackRejectsMetadataAddress()
    {
        using var handler = WebhookDispatcher.CreatePinnedSocketsHttpHandler(allowLoopback: false, allowPrivateNetworkRequests: false);
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.ConnectCallback.Should().NotBeNull();
        using var client = new HttpClient(handler, disposeHandler: false);

        var act = async () => await client.GetAsync("http://169.254.169.254/latest/meta-data/");

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        thrown.Which.InnerException.Should().BeOfType<SecurityException>();
    }

    [Test]
    public void Constructor_SupportsConfigurableTimeout()
    {
        using var client = new HttpClient();
        var customDispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(45));

        customDispatcher.Timeout.Should().Be(TimeSpan.FromSeconds(45));
    }

    [TestCase(double.PositiveInfinity, "Infinity")]
    [TestCase(double.NegativeInfinity, "-Infinity")]
    [TestCase(double.NaN, "NaN")]
    public async Task DispatchAsync_WhenPayloadContainsNonFiniteDouble_SerializesSuccessfullyAndDispatches(double nonFiniteValue, string expectedToken)
    {
        var payload = new
        {
            eventType = "OnGrab",
            ratio = nonFiniteValue,
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", payload);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(1);

        var request = this.handler.SentRequests.Single();
        var body = await request.Content!.ReadAsStringAsync();
        body.Should().Contain($"\"ratio\":\"{expectedToken}\"");
    }

    [Test]
    public async Task DispatchAsync_WhenRedirectToProhibitedTarget_BlocksRedirectAndReturnsFalse()
    {
        this.handler.ResponseFactory = req =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Redirect);
            res.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
            return res;
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(1);
    }

    [Test]
    public async Task DispatchAsync_WhenRedirectToPrivateIp_BlocksRedirectAndReturnsFalse()
    {
        this.handler.ResponseFactory = req =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Redirect);
            res.Headers.Location = new Uri("http://10.0.0.1/internal");
            return res;
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(1);
    }

    [Test]
    public async Task DispatchAsync_WhenRedirectToSafeTarget_FollowsRedirectAndReturnsTrue()
    {
        var count = 0;
        this.handler.ResponseFactory = req =>
        {
            count++;
            if (count == 1)
            {
                var res = new HttpResponseMessage(HttpStatusCode.Redirect);
                res.Headers.Location = new Uri("https://example.com/redirected-webhook");
                return res;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" });

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(2);
        this.handler.SentRequests[1].RequestUri.Should().Be(new Uri("https://example.com/redirected-webhook"));
        this.handler.SentRequests[1].Method.Should().Be(HttpMethod.Get);
        this.handler.SentRequests[1].Content.Should().BeNull();
    }

    [Test]
    public async Task DispatchAsync_WhenRedirect302_StripsAuthorizationAndUsesGetWithoutBody()
    {
        var count = 0;
        var customHeaders = "{\"Authorization\":\"Bearer secret-token\",\"X-Custom-Header\":\"keep-me\"}";
        this.handler.ResponseFactory = req =>
        {
            count++;
            if (count == 1)
            {
                var res = new HttpResponseMessage(HttpStatusCode.Redirect);
                res.Headers.Location = new Uri("https://example.com/redirected-webhook");
                return res;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, customHeaders);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(2);

        var redirectRequest = this.handler.SentRequests[1];
        redirectRequest.Method.Should().Be(HttpMethod.Get);
        redirectRequest.Content.Should().BeNull();
        redirectRequest.Headers.Contains("Authorization").Should().BeFalse();
        redirectRequest.Headers.GetValues("X-Custom-Header").Should().ContainSingle().Which.Should().Be("keep-me");
    }

    [Test]
    public async Task DispatchAsync_WhenRedirectToDifferentHost_BlocksRedirectAndReturnsFalse()
    {
        this.handler.ResponseFactory = _ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Redirect);
            res.Headers.Location = new Uri("https://example.org/collect");
            return res;
        };

        var result = await this.dispatcher.DispatchAsync(
            "https://example.com/notify",
            new { eventType = "Test" },
            "{\"Authorization\":\"Bearer secret\"}");

        result.Should().BeFalse();
        this.handler.SentRequests.Should().HaveCount(1);
    }

    [Test]
    public async Task DispatchAsync_WhenRedirect307_StripsAuthorizationButKeepsPostBody()
    {
        var count = 0;
        var customHeaders = "{\"Authorization\":\"Bearer secret-token\"}";
        this.handler.ResponseFactory = req =>
        {
            count++;
            if (count == 1)
            {
                var res = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                res.Headers.Location = new Uri("https://example.com/final");
                return res;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await this.dispatcher.DispatchAsync("https://example.com/webhook", new { eventType = "Test" }, customHeaders);

        result.Should().BeTrue();
        this.handler.SentRequests.Should().HaveCount(2);

        var redirectRequest = this.handler.SentRequests[1];
        redirectRequest.Method.Should().Be(HttpMethod.Post);
        redirectRequest.Content.Should().NotBeNull();
        redirectRequest.Headers.Contains("Authorization").Should().BeFalse();
    }

    [Test]
    public void SanitizeCustomHeadersForRedirect_RemovesAuthorizationFromJsonAndLineFormats()
    {
        WebhookDispatcher.SanitizeCustomHeadersForRedirect("{\"Authorization\":\"Bearer x\",\"X-Trace\":\"1\"}")
            .Should().Be("{\"X-Trace\":\"1\"}");

        WebhookDispatcher.SanitizeCustomHeadersForRedirect("Authorization: Bearer x\r\nX-Trace: 1")
            .Should().Be("X-Trace: 1");
    }

    [Test]
    public void ExtractRetryAfter_WithRetryAfterSecondsHeader_ReturnsTimeSpan()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "30");

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        retryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void ExtractRetryAfter_WithXRetryAfterHeader_ReturnsTimeSpan()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("X-Retry-After", "45");

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        retryAfter.Should().Be(TimeSpan.FromSeconds(45));
    }

    [TestCase("3600", 60)]
    [TestCase("86400", 60)]
    [TestCase("5", 5)]
    [TestCase("45", 45)]
    public void ExtractRetryAfter_WithVariousRetryAfterHeaderValues_CapsAtMaxRetryAfter(string headerVal, int expectedSeconds)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", headerVal);

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        retryAfter.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [TestCase("7200", 60)]
    [TestCase("10", 10)]
    public void ExtractRetryAfter_WithVariousXRetryAfterHeaderValues_CapsAtMaxRetryAfter(string headerVal, int expectedSeconds)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("X-Retry-After", headerVal);

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        retryAfter.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    public void ExtractRetryAfter_WithRetryAfterDateExceedingMax_CapsAtMaxRetryAfter()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var futureDate = DateTimeOffset.UtcNow.AddHours(2);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(futureDate);

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        retryAfter.Should().Be(WebhookDispatcher.MaxRetryAfter);
    }

    [TestCase("{\"retry_after\": 5}", 5)]
    [TestCase("{\"retry_after\": 3600}", 60)]
    [TestCase("{\"parameters\": {\"retry_after\": 10}}", 10)]
    [TestCase("{\"parameters\": {\"retry_after\": 86400}}", 60)]
    public void ExtractRetryAfter_WithJsonBodyRetryAfter_ParsesAndCapsAtMaxRetryAfter(string jsonBody, int expectedSeconds)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
        };

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        retryAfter.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    public async Task CreateRetryPolicy_When429ResponseHasLargeRetryAfter_CapsSleepDuration()
    {
        var attempts = 0;
        var sleepDurations = new List<TimeSpan>();

        var policy = WebhookDispatcher.CreateRetryPolicy(
            retryCount: 2,
            onRetry: (outcome, timespan, attempt, context) =>
            {
                sleepDurations.Add(timespan);
            });

        using var response429 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response429.Headers.TryAddWithoutValidation("Retry-After", "86400");

        try
        {
            await policy.ExecuteAsync(
                ct =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        return Task.FromResult(response429);
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                },
                CancellationToken.None);
        }
        catch
        {
        }

        sleepDurations.Should().NotBeEmpty();
        sleepDurations[0].Should().Be(WebhookDispatcher.MaxRetryAfter);
    }
}
