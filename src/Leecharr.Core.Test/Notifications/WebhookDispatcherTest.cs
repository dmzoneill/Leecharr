// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using Polly;
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
    #region Header Sanitization Tests

    [Test]
    public void SanitizeHeadersForLogging_WithBearerToken_RedactsTokenAndLeavesOnlyHeaderName()
    {
        var input = "Authorization: Bearer sk-1234567890abcdef";
        var sanitized = WebhookDispatcher.SanitizeHeadersForLogging(input);
        sanitized.Should().Be("Authorization");
        sanitized.Should().NotContain("Bearer");
        sanitized.Should().NotContain("sk-1234567890abcdef");
    }

    [Test]
    public void SanitizeHeadersForLogging_WithApiKey_RedactsValue()
    {
        var input = "X-Api-Key=super_secret_token_value";
        var sanitized = WebhookDispatcher.SanitizeHeadersForLogging(input);
        sanitized.Should().Be("X-Api-Key");
        sanitized.Should().NotContain("super_secret_token_value");
    }

    [Test]
    public void SanitizeHeadersForLogging_WithJsonHeaders_RedactsValuesAndReturnsOnlyKeys()
    {
        var input = "{\"Authorization\":\"Bearer secret-token\",\"X-Api-Key\":\"secret-key\"}";
        var sanitized = WebhookDispatcher.SanitizeHeadersForLogging(input);
        sanitized.Should().Be("Authorization, X-Api-Key");
        sanitized.Should().NotContain("secret-token");
        sanitized.Should().NotContain("secret-key");
    }

    [Test]
    public void SanitizeHeadersForLogging_WithMultipleLineHeaders_ExtractsOnlyKeys()
    {
        var input = "Authorization: Bearer key1\r\nX-Secret: secret2";
        var sanitized = WebhookDispatcher.SanitizeHeadersForLogging(input);
        sanitized.Should().Be("Authorization, X-Secret");
        sanitized.Should().NotContain("key1");
        sanitized.Should().NotContain("secret2");
    }

    [Test]
    public void SanitizeHeadersForLogging_WhenInputNullOrEmpty_ReturnsEmpty()
    {
        WebhookDispatcher.SanitizeHeadersForLogging(null).Should().Be(string.Empty);
        WebhookDispatcher.SanitizeHeadersForLogging(string.Empty).Should().Be(string.Empty);
        WebhookDispatcher.SanitizeHeadersForLogging("   ").Should().Be(string.Empty);
    }

    #endregion
}
