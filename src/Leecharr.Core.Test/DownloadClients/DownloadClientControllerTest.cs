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

    [Test]
    public async Task Test_QBittorrent_WithCredentials_Success()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }

            if (req.RequestUri.AbsolutePath == "/api/v2/app/webapiVersion")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("2.8.19") };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBitAuth",
            ClientType = "qBittorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "secretpassword",
            Enable = true,
        };
        this.repository.Get(1).Returns(clientDef);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var result = await controller.Test(1);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as DownloadClientTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult.Message.Should().Contain("v2.8.19");
        requests.Should().HaveCount(2);
        requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v2/auth/login");
        requests[1].RequestUri!.AbsolutePath.Should().Be("/api/v2/app/webapiVersion");
    }

    [Test]
    public async Task Test_QBittorrent_WithInvalidCredentials_ReturnsFailure()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") };
            }

            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBitAuth",
            ClientType = "qBittorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "wrongpassword",
            Enable = true,
        };
        this.repository.Get(1).Returns(clientDef);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var result = await controller.Test(1);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as DownloadClientTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeFalse();
        testResult.Message.Should().Contain("Invalid username or password");
    }

    [Test]
    public async Task Test_Transmission_WithCredentials_Success()
    {
        HttpRequestMessage capturedRequest = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.Conflict);
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 2,
            Name = "TransAuth",
            ClientType = "Transmission",
            Host = "localhost",
            Port = 9091,
            Username = "user",
            Password = "password",
            Enable = true,
        };
        this.repository.Get(2).Returns(clientDef);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var result = await controller.Test(2);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as DownloadClientTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult.Message.Should().Contain("Transmission RPC endpoint reachable");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
    }

    [Test]
    public async Task Test_Transmission_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 2,
            Name = "TransAuth",
            ClientType = "Transmission",
            Host = "localhost",
            Port = 9091,
            Username = "user",
            Password = "wrongpassword",
            Enable = true,
        };
        this.repository.Get(2).Returns(clientDef);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var result = await controller.Test(2);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as DownloadClientTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeFalse();
        testResult.Message.Should().Contain("Invalid username or password");
    }

    [Test]
    public async Task Test_Deluge_WithCredentials_Success()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (requests.Count == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"result\":true,\"error\":null,\"id\":1}", System.Text.Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":true,\"error\":null,\"id\":1}", System.Text.Encoding.UTF8, "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 3,
            Name = "DelugeAuth",
            ClientType = "Deluge",
            Host = "localhost",
            Port = 8112,
            Password = "delugepassword",
            Enable = true,
        };
        this.repository.Get(3).Returns(clientDef);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var result = await controller.Test(3);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as DownloadClientTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult.Message.Should().Contain("Deluge JSON-RPC connected successfully");
        requests.Should().HaveCount(2);
    }

    [Test]
    public async Task Test_Deluge_WithInvalidPassword_ReturnsFailure()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"result\":false,\"error\":null,\"id\":1}", System.Text.Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 3,
            Name = "DelugeAuth",
            ClientType = "Deluge",
            Host = "localhost",
            Port = 8112,
            Password = "wrongpassword",
            Enable = true,
        };
        this.repository.Get(3).Returns(clientDef);

        var controller = new DownloadClientController(this.repository, this.torrentService, httpClient);
        var result = await controller.Test(3);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as DownloadClientTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeFalse();
        testResult.Message.Should().Contain("Invalid password");
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_Deluge_WithCredentials_AuthenticatesAndReturnsItems()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (requests.Count == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"result\":true,\"error\":null,\"id\":1}", System.Text.Encoding.UTF8, "application/json"),
                };
            }

            var torrentsJson = "{\"result\":{\"3333333333333333333333333333333333333333\":{\"name\":\"Arch Linux\",\"total_size\":3000,\"progress\":50.0,\"state\":\"Downloading\",\"save_path\":\"/data\",\"label\":\"linux\"}},\"error\":null,\"id\":1}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(torrentsJson, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 3,
            Name = "DelugeAuth",
            ClientType = "Deluge",
            Host = "localhost",
            Port = 8112,
            Password = "delugepassword",
            Enable = true,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(clientDef, httpClient);

        items.Should().HaveCount(1);
        items[0].InfoHash.Should().Be("3333333333333333333333333333333333333333");
        items[0].Name.Should().Be("Arch Linux");
        items[0].Progress.Should().Be(0.5);
        items[0].Category.Should().Be("linux");
        requests.Should().HaveCount(2);
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_Transmission_WithCredentials_SendsAuthAndReturnsItems()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            var json = "{\"arguments\":{\"torrents\":[{\"id\":1,\"hashString\":\"4444444444444444444444444444444444444444\",\"name\":\"Debian\",\"totalSize\":4000,\"percentDone\":1.0,\"status\":6,\"downloadDir\":\"/iso\"}]},\"result\":\"success\"}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler);

        var clientDef = new DownloadClientDefinition
        {
            Id = 4,
            Name = "TransAuth",
            ClientType = "Transmission",
            Host = "localhost",
            Port = 9091,
            Username = "user",
            Password = "pass",
            Enable = true,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(clientDef, httpClient);

        items.Should().HaveCount(1);
        items[0].InfoHash.Should().Be("4444444444444444444444444444444444444444");
        items[0].Name.Should().Be("Debian");
        requests.Should().HaveCount(1);
        requests[0].Headers.Authorization.Should().NotBeNull();
        requests[0].Headers.Authorization!.Scheme.Should().Be("Basic");
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
