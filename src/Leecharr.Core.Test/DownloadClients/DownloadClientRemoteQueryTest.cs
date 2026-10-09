// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.DownloadClients;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;

namespace Leecharr.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientRemoteQueryTest
{
    [Test]
    public async Task QueryRemoteClientItemsAsync_QBittorrent_DisposesAllHttpResponses()
    {
        var loginResponse = new DisposableHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Ok."),
        };
        var infoResponse = new DisposableHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v2/auth/login", StringComparison.Ordinal))
            {
                return loginResponse;
            }

            return infoResponse;
        });

        using var httpClient = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Name = "QBit",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, httpClient);

        items.Should().BeEmpty();
        loginResponse.IsDisposed.Should().BeTrue();
        infoResponse.IsDisposed.Should().BeTrue();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_Transmission409Retry_DisposesConflictResponse()
    {
        var conflictResponse = new DisposableHttpResponseMessage(HttpStatusCode.Conflict);
        conflictResponse.Headers.Add("X-Transmission-Session-Id", "session-1");
        var successResponse = new DisposableHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"result\":\"success\",\"arguments\":{\"torrents\":[]}}",
                Encoding.UTF8,
                "application/json"),
        };

        var callCount = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1 ? conflictResponse : successResponse;
        });

        using var httpClient = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Name = "Transmission",
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, httpClient);

        items.Should().BeEmpty();
        conflictResponse.IsDisposed.Should().BeTrue();
        successResponse.IsDisposed.Should().BeTrue();
    }

    [Test]
    public async Task PauseTorrentAsync_TransmissionRpcError_ReturnsFalse()
    {
        var rpcResponse = new DisposableHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"result\":\"invalid argument\"}",
                Encoding.UTF8,
                "application/json"),
        };

        var handler = new MockHttpMessageHandler(_ => rpcResponse);
        using var httpClient = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Name = "Transmission",
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
        };

        var result = await DownloadClientRemoteQuery.PauseTorrentAsync(
            client,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            httpClient);

        result.Should().BeFalse();
        rpcResponse.IsDisposed.Should().BeTrue();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_TransmissionRpcError_ReturnsEmpty()
    {
        var rpcResponse = new DisposableHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"result\":\"unauthorized session id\"}",
                Encoding.UTF8,
                "application/json"),
        };

        var handler = new MockHttpMessageHandler(_ => rpcResponse);
        using var httpClient = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Name = "Transmission",
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, httpClient);

        items.Should().BeEmpty();
        rpcResponse.IsDisposed.Should().BeTrue();
    }

    [Test]
    public async Task PauseTorrentAsync_QBittorrentNotFoundFallback_DisposesBothResponses()
    {
        var pauseResponse = new DisposableHttpResponseMessage(HttpStatusCode.NotFound);
        var stopResponse = new DisposableHttpResponseMessage(HttpStatusCode.OK);
        var callCount = 0;

        var handler = new MockHttpMessageHandler(req =>
        {
            callCount++;
            if (req.RequestUri!.AbsolutePath.Contains("/api/v2/torrents/pause", StringComparison.Ordinal))
            {
                return pauseResponse;
            }

            if (req.RequestUri.AbsolutePath.Contains("/api/v2/torrents/stop", StringComparison.Ordinal))
            {
                return stopResponse;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Name = "QBit",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
        };

        var result = await DownloadClientRemoteQuery.PauseTorrentAsync(
            client,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            httpClient);

        result.Should().BeTrue();
        pauseResponse.IsDisposed.Should().BeTrue();
        stopResponse.IsDisposed.Should().BeTrue();
    }

    private sealed class DisposableHttpResponseMessage : HttpResponseMessage
    {
        public DisposableHttpResponseMessage(HttpStatusCode statusCode)
            : base(statusCode)
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
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
