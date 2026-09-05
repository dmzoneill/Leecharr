// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.DownloadClients;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientSyncControllerTest
{
    private IDownloadClientRepository clientRepository = null!;
    private ITorrentService torrentService = null!;

    [SetUp]
    public void SetUp()
    {
        this.clientRepository = Substitute.For<IDownloadClientRepository>();
        this.torrentService = Substitute.For<ITorrentService>();
    }

    [Test]
    public async Task Sync_WhenNoEnabledClients_ReturnsZeroCounts()
    {
        this.clientRepository.GetEnabled().Returns(new List<DownloadClientDefinition>());
        var controller = new DownloadClientSyncController(this.clientRepository, this.torrentService);

        var actionResult = await controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
        result.TotalCount.Should().Be(0);
        result.Added.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Message.Should().Be("Download client sync completed successfully (0 torrent(s) imported).");
    }

    [Test]
    public async Task Sync_WhenClientHasTorrents_QueriesAndImportsUntrackedTorrents()
    {
        var json = "[{\"hash\":\"aabbccddeeff00112233445566778899aabbccdd\",\"name\":\"Ubuntu 22.04\",\"size\":1000000,\"progress\":1.0,\"state\":\"seeding\",\"save_path\":\"/downloads/linux\",\"category\":\"iso\"}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);

        var controller = new DownloadClientSyncController(this.clientRepository, this.torrentService, httpClient);

        var clientDef = new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBit",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Category = "default-cat",
            Enable = true,
        };
        this.clientRepository.GetEnabled().Returns(new List<DownloadClientDefinition> { clientDef });
        this.torrentService.GetByInfoHash(Arg.Any<string>()).Returns((Torrent)null!);

        var actionResult = await controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(1);
        result.TotalCount.Should().Be(1);
        result.Added.Should().Be(1);
        result.Skipped.Should().Be(0);

        await this.torrentService.Received(1).AddFromMagnetAsync(
            "magnet:?xt=urn:btih:aabbccddeeff00112233445566778899aabbccdd",
            "iso",
            "/downloads/linux",
            false);
    }

    [Test]
    public async Task Sync_WhenTorrentAlreadyTracked_SkipsImport()
    {
        var json = "[{\"hash\":\"aabbccddeeff00112233445566778899aabbccdd\",\"name\":\"Ubuntu 22.04\",\"size\":1000000,\"progress\":1.0,\"state\":\"seeding\",\"save_path\":\"/downloads/linux\",\"category\":\"iso\"}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);

        var controller = new DownloadClientSyncController(this.clientRepository, this.torrentService, httpClient);

        var clientDef = new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBit",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Enable = true,
        };
        this.clientRepository.GetEnabled().Returns(new List<DownloadClientDefinition> { clientDef });
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(new Torrent { Id = 10, InfoHash = "aabbccddeeff00112233445566778899aabbccdd" });

        var actionResult = await controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
        result.TotalCount.Should().Be(1);
        result.Skipped.Should().Be(1);

        await this.torrentService.DidNotReceive().AddFromMagnetAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>());
    }

    [Test]
    public async Task Sync_WhenClientFails_HandlesGracefully()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(handler);

        var controller = new DownloadClientSyncController(this.clientRepository, this.torrentService, httpClient);

        var clientDef = new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBit",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Enable = true,
        };
        this.clientRepository.GetEnabled().Returns(new List<DownloadClientDefinition> { clientDef });

        var actionResult = await controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
        result.TotalCount.Should().Be(0);
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
