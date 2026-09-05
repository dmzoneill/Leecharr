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
using Leecharr.Api.V1.Torrents;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientControllerTest
{
    private IDownloadClientRepository repository = null!;
    private ITorrentService torrentService = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<IDownloadClientRepository>();
        this.torrentService = Substitute.For<ITorrentService>();
    }

    [Test]
    public void GetAll_ReturnsAllClientsMappedToResources()
    {
        var definitions = new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "Client1", ClientType = "qBittorrent", Host = "localhost", Port = 8080, Category = "movies", Enable = true },
            new() { Id = 2, Name = "Client2", ClientType = "Transmission", Host = "localhost", Port = 9091, Category = "tv", Enable = false },
        };
        this.repository.All().Returns(definitions);

        var controller = new DownloadClientController(this.repository, this.torrentService);
        var result = controller.GetAll();

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var list = okResult!.Value as List<DownloadClientResource>;
        list.Should().NotBeNull();
        list!.Count.Should().Be(2);
        list[0].Category.Should().Be("movies");
        list[0].Enabled.Should().BeTrue();
        list[1].Category.Should().Be("tv");
        list[1].Enabled.Should().BeFalse();
    }

    [Test]
    public void Get_WhenExists_ReturnsClient()
    {
        var definition = new DownloadClientDefinition { Id = 1, Name = "Client1", ClientType = "qBittorrent", Host = "localhost", Port = 8080, Category = "movies", Enable = true };
        this.repository.Get(1).Returns(definition);

        var controller = new DownloadClientController(this.repository, this.torrentService);
        var result = controller.Get(1);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var res = okResult!.Value as DownloadClientResource;
        res.Should().NotBeNull();
        res!.Name.Should().Be("Client1");
        res.Category.Should().Be("movies");
    }

    [Test]
    public void Get_WhenNotFound_ReturnsNotFound()
    {
        this.repository.Get(99).Returns((DownloadClientDefinition)null!);

        var controller = new DownloadClientController(this.repository, this.torrentService);
        var result = controller.Get(99);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task ImportTorrent_Single_WhenUntracked_QueriesRemoteItem_PreservesSavePathAndCategory()
    {
        var json = "[{\"hash\":\"1122334455667788990011223344556677889900\",\"name\":\"Fedora 38\",\"size\":2000000,\"progress\":1.0,\"state\":\"seeding\",\"save_path\":\"/data/iso\",\"category\":\"distro\"}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition { Id = 1, Name = "Client1", ClientType = "qBittorrent", Host = "localhost", Port = 8080, Category = "default-cat", Enable = true };
        this.repository.Get(1).Returns(clientDef);
        this.torrentService.GetByInfoHash("1122334455667788990011223344556677889900").Returns((Torrent)null!);

        var addedTorrent = new Torrent { Id = 5, InfoHash = "1122334455667788990011223344556677889900", Name = "Fedora 38" };
        this.torrentService.AddFromMagnetAsync(
            "magnet:?xt=urn:btih:1122334455667788990011223344556677889900",
            "distro",
            "/data/iso",
            false).Returns(Task.FromResult(addedTorrent));

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var result = await controller.ImportTorrent(1, "1122334455667788990011223344556677889900");

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var resource = okResult!.Value as TorrentResource;
        resource.Should().NotBeNull();
        resource!.Id.Should().Be(5);
    }

    [Test]
    public async Task ImportTorrents_Bulk_WithInfoHashes_QueriesRemoteItems_PreservesSavePath()
    {
        var json = "[{\"hash\":\"1111111111111111111111111111111111111111\",\"name\":\"Item 1\",\"size\":1000,\"progress\":1.0,\"state\":\"seeding\",\"save_path\":\"/path/1\",\"category\":\"cat1\"}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition { Id = 1, Name = "Client1", ClientType = "qBittorrent", Host = "localhost", Port = 8080, Enable = true };
        this.repository.Get(1).Returns(clientDef);
        this.torrentService.GetByInfoHash("1111111111111111111111111111111111111111").Returns((Torrent)null!);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var request = new ImportRequest
        {
            InfoHashes = new List<string> { "1111111111111111111111111111111111111111" },
        };

        var result = await controller.ImportTorrents(1, request);
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var syncResult = okResult!.Value as SyncResultResource;
        syncResult.Should().NotBeNull();
        syncResult!.SyncedCount.Should().Be(1);

        await this.torrentService.Received(1).AddFromMagnetAsync(
            "magnet:?xt=urn:btih:1111111111111111111111111111111111111111",
            "cat1",
            "/path/1",
            false);
    }

    [Test]
    public async Task ImportTorrents_Bulk_WithHashes_QueriesRemoteItems_PreservesSavePath()
    {
        var json = "[{\"hash\":\"2222222222222222222222222222222222222222\",\"name\":\"Item 2\",\"size\":2000,\"progress\":1.0,\"state\":\"seeding\",\"save_path\":\"/path/2\",\"category\":\"cat2\"}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition { Id = 1, Name = "Client1", ClientType = "qBittorrent", Host = "localhost", Port = 8080, Enable = true };
        this.repository.Get(1).Returns(clientDef);
        this.torrentService.GetByInfoHash("2222222222222222222222222222222222222222").Returns((Torrent)null!);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var request = new ImportRequest
        {
            Hashes = new List<string> { "2222222222222222222222222222222222222222" },
        };

        var result = await controller.ImportTorrents(1, request);
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var syncResult = okResult!.Value as SyncResultResource;
        syncResult.Should().NotBeNull();
        syncResult!.SyncedCount.Should().Be(1);

        await this.torrentService.Received(1).AddFromMagnetAsync(
            "magnet:?xt=urn:btih:2222222222222222222222222222222222222222",
            "cat2",
            "/path/2",
            false);
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
