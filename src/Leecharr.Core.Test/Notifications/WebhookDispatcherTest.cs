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
        this.fastRetryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(1));

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
}
