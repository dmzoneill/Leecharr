// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrSyncControllerTest
{
    private IArrConnectionRepository arrRepository = null!;
    private ITorrentService torrentService = null!;
    private ArrSyncController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.arrRepository = Substitute.For<IArrConnectionRepository>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.controller = new ArrSyncController(this.arrRepository, this.torrentService);
    }

    [Test]
    public async Task Sync_WhenNoConnections_ReturnsZeroCounts()
    {
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var actionResult = await this.controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
        result.TotalCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.Added.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Message.Should().Be("Arr sync completed successfully (0/0 connected).");
    }

    [Test]
    public async Task Sync_WhenConnectionFails_ReturnsFailedCount()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                Name = "Sonarr",
                Url = "http://127.0.0.1:59999",
                ApiKey = "dummy-key",
            },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var actionResult = await this.controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
        result.TotalCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Added.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(1);
        result.Message.Should().Be("Arr sync completed successfully (0/1 connected).");
    }

    [TestCase("Lidarr", "/api/v1/system/status")]
    [TestCase("Readarr", "/api/v1/system/status")]
    [TestCase("Prowlarr", "/api/v1/system/status")]
    [TestCase("Sonarr", "/api/v3/system/status")]
    [TestCase("Radarr", "/api/v3/system/status")]
    public async Task Sync_WhenConfiguredArrType_ProbesPrimaryEndpointFirst(string arrType, string expectedPath)
    {
        var requestedUris = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUris.Add(req.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var syncController = new ArrSyncController(this.arrRepository, this.torrentService, httpClient);

        var connections = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                Name = arrType,
                ArrType = arrType,
                Url = "http://127.0.0.1:8989",
                ApiKey = "secret-key",
            },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var actionResult = await syncController.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        requestedUris.Should().ContainSingle().Which.Should().Be(expectedPath);
    }

    [Test]
    public async Task Sync_WhenPrimaryEndpointFails_FallsBackToSecondaryEndpoint()
    {
        var requestedUris = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUris.Add(req.RequestUri!.PathAndQuery);
            if (req.RequestUri.PathAndQuery.Contains("v3"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var syncController = new ArrSyncController(this.arrRepository, this.torrentService, httpClient);

        var connections = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                Name = "SonarrLegacy",
                ArrType = "Sonarr",
                Url = "http://127.0.0.1:8989",
                ApiKey = "secret-key",
            },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var actionResult = await syncController.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        requestedUris.Should().Equal("/api/v3/system/status", "/api/v1/system/status");
    }

    [Test]
    public void SyncResultResource_PropertiesCanBeSetAndRetrieved()
    {
        var resource = new SyncResultResource
        {
            Success = true,
            SyncedCount = 3,
            TotalCount = 4,
            FailedCount = 1,
            Added = 3,
            Skipped = 0,
            Failed = 1,
            Message = "Test message",
        };

        resource.Success.Should().BeTrue();
        resource.SyncedCount.Should().Be(3);
        resource.TotalCount.Should().Be(4);
        resource.FailedCount.Should().Be(1);
        resource.Added.Should().Be(3);
        resource.Skipped.Should().Be(0);
        resource.Failed.Should().Be(1);
        resource.Message.Should().Be("Test message");
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
