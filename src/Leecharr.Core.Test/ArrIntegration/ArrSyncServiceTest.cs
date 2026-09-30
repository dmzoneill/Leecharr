// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Messaging.Commands;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrSyncServiceTest
{
    private IArrConnectionRepository arrRepository = null!;

    [SetUp]
    public void SetUp()
    {
        this.arrRepository = Substitute.For<IArrConnectionRepository>();
    }

    [Test]
    public async Task ExecuteAsync_WhenInvoked_ExecutesSyncAsync()
    {
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var service = new ArrSyncService(this.arrRepository);
        var command = new SyncArrCommand { AppType = "Radarr", InstanceId = 1 };

        await service.ExecuteAsync(command);

        this.arrRepository.Received(1).GetEnabled();
    }

    [Test]
    public void Execute_WhenInvoked_ExecutesSync()
    {
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var service = new ArrSyncService(this.arrRepository);
        var command = new SyncArrCommand();

        service.Execute(command);

        this.arrRepository.Received(1).GetEnabled();
    }

    [Test]
    public async Task SyncAsync_WhenNoEnabledConnections_ReturnsZero()
    {
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var service = new ArrSyncService(this.arrRepository);
        var result = await service.SyncAsync();

        result.Should().Be(0);
    }

    [Test]
    public async Task SyncAsync_FiltersByInstanceId_WhenSpecified()
    {
        var requestedUris = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUris.Add(req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var service = new ArrSyncService(this.arrRepository, httpClient);

        var connections = new List<ArrConnectionDefinition>
        {
            new() { Id = 1, Name = "Sonarr 1", ArrType = "Sonarr", Url = "http://127.0.0.1:8989", ApiKey = "key1" },
            new() { Id = 2, Name = "Sonarr 2", ArrType = "Sonarr", Url = "http://127.0.0.1:8990", ApiKey = "key2" },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var count = await service.SyncAsync(instanceId: 2);

        count.Should().Be(1);
        requestedUris.Should().ContainSingle(u => u.Contains(":8990"));
    }

    [Test]
    public async Task SyncAsync_FiltersByAppType_WhenSpecified()
    {
        var requestedUris = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUris.Add(req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var service = new ArrSyncService(this.arrRepository, httpClient);

        var connections = new List<ArrConnectionDefinition>
        {
            new() { Id = 1, Name = "Sonarr", ArrType = "Sonarr", Url = "http://127.0.0.1:8989", ApiKey = "key1" },
            new() { Id = 2, Name = "Radarr", ArrType = "Radarr", Url = "http://127.0.0.1:7878", ApiKey = "key2" },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var count = await service.SyncAsync(appType: "Radarr");

        count.Should().Be(1);
        requestedUris.Should().ContainSingle(u => u.Contains(":7878"));
    }

    [TestCase("Lidarr", "/api/v1/system/status")]
    [TestCase("Readarr", "/api/v1/system/status")]
    [TestCase("Prowlarr", "/api/v1/system/status")]
    [TestCase("Sonarr", "/api/v3/system/status")]
    [TestCase("Radarr", "/api/v3/system/status")]
    public async Task SyncAsync_ProbesEndpoints_AndCountsSuccessfulConnections(string arrType, string expectedPath)
    {
        var requestedUris = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUris.Add(req.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var service = new ArrSyncService(this.arrRepository, httpClient);

        var connections = new List<ArrConnectionDefinition>
        {
            new() { Id = 1, Name = arrType, ArrType = arrType, Url = "http://127.0.0.1:8080", ApiKey = "secret" },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var count = await service.SyncAsync();

        count.Should().Be(1);
        requestedUris.Should().ContainSingle().Which.Should().Be(expectedPath);
    }

    [Test]
    public async Task SyncAsync_WhenHttpFails_HandlesGracefullyAndReturnsZero()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(handler);
        var service = new ArrSyncService(this.arrRepository, httpClient);

        var connections = new List<ArrConnectionDefinition>
        {
            new() { Id = 1, Name = "Sonarr", ArrType = "Sonarr", Url = "http://127.0.0.1:8989", ApiKey = "key" },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var count = await service.SyncAsync();

        count.Should().Be(0);
    }

    [Test]
    public void SyncArrCommand_Properties_CanBeSetAndRetrieved()
    {
        var cmd = new SyncArrCommand
        {
            AppType = "Sonarr",
            InstanceId = 42,
        };

        cmd.AppType.Should().Be("Sonarr");
        cmd.InstanceId.Should().Be(42);
        cmd.Name.Should().Be("SyncArrCommand");
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
