// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.DownloadClients;
using Leecharr.Api.V1.Torrents;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.DownloadClientsTests;

[TestFixture]
public class ClientImportAndProxyTest
{
    private IDownloadClientRepository repository = null!;
    private ITorrentService torrentService = null!;
    private ISafeHttpClientService safeHttpClientService = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<IDownloadClientRepository>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenClientIsQBittorrent_DispatchesToQBittorrentEndpoints()
    {
        var requestedUrls = new List<string>();
        var qbitJson = "[{\"hash\":\"1111111111111111111111111111111111111111\",\"name\":\"qBit Torrent\",\"size\":5000,\"progress\":0.5,\"state\":\"downloading\",\"save_path\":\"/downloads\",\"category\":\"tv\"}]";

        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUrls.Add(req.RequestUri!.ToString());
            if (req.RequestUri.AbsolutePath.Contains("/api/v2/auth/login"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(qbitJson, Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 1,
            Name = "QBitClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        items.Should().HaveCount(1);
        items[0].InfoHash.Should().Be("1111111111111111111111111111111111111111");
        requestedUrls.Should().Contain(u => u.Contains("/api/v2/auth/login"));
        requestedUrls.Should().Contain(u => u.Contains("/api/v2/torrents/info"));
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenClientIsTransmission_DispatchesToTransmissionRpcEndpoint()
    {
        var requestedUrls = new List<string>();
        var transmissionJson = "{\"arguments\":{\"torrents\":[{\"hashString\":\"2222222222222222222222222222222222222222\",\"name\":\"Transmission Torrent\",\"totalSize\":10000,\"percentDone\":1.0,\"status\":6,\"downloadDir\":\"/media\"}]}}";

        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUrls.Add(req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(transmissionJson, Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 2,
            Name = "TransClient",
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        items.Should().HaveCount(1);
        items[0].InfoHash.Should().Be("2222222222222222222222222222222222222222");
        items[0].SavePath.Should().Be("/media");
        requestedUrls.Should().ContainSingle(u => u.Contains("/transmission/rpc"));
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenClientIsDeluge_DispatchesToDelugeJsonEndpoint()
    {
        var requestedUrls = new List<string>();
        var delugeJson = "{\"result\":{\"3333333333333333333333333333333333333333\":{\"name\":\"Deluge Torrent\",\"total_size\":20000,\"progress\":75.5,\"state\":\"Active\",\"save_path\":\"/torrents\",\"label\":\"movies\"}}}";

        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUrls.Add(req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(delugeJson, Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 3,
            Name = "DelugeClient",
            ClientType = "Deluge",
            Host = "127.0.0.1",
            Port = 8112,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        items.Should().HaveCount(1);
        items[0].InfoHash.Should().Be("3333333333333333333333333333333333333333");
        items[0].Category.Should().Be("movies");
        requestedUrls.Should().ContainSingle(u => u.Contains("/json"));
    }

    [TestCase("qbittorrent")]
    [TestCase("QBITTORRENT")]
    [TestCase("Transmission")]
    [TestCase("deluge")]
    public async Task QueryRemoteClientItemsAsync_ClientTypeComparisonIsCaseInsensitive(string clientType)
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"arguments\":{\"torrents\":[]},\"result\":{}}", Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 10,
            Name = "CaseClient",
            ClientType = clientType,
            Host = "127.0.0.1",
            Port = 8080,
        };

        var act = async () => await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenClientTypeUnknown_ReturnsEmptyListWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 4,
            Name = "UnknownClient",
            ClientType = "SomeRandomCustomClient",
            Host = "127.0.0.1",
            Port = 5555,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);
        items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenQBittorrentCredentialsProvided_SendsFormUrlEncodedPost()
    {
        string capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v2/auth/login"))
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Username = "myuser",
            Password = "mypassword",
        };

        await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("username=myuser");
        capturedBody.Should().Contain("password=mypassword");
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenQBittorrentReturnsFailsBody_AbortsAndReturnsEmptyItems()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v2/auth/login"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"hash\":\"abc\"}]", Encoding.UTF8, "application/json") };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Username = "baduser",
            Password = "badpassword",
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);
        items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenTransmissionCredentialsProvided_SendsBasicAuthHeader()
    {
        string capturedAuthHeader = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedAuthHeader = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"arguments\":{\"torrents\":[]}}", Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
            Username = "transmissionUser",
            Password = "transmissionPassword",
        };

        await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        var expectedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes("transmissionUser:transmissionPassword"));
        capturedAuthHeader.Should().Be($"Basic {expectedCreds}");
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenTransmissionReturnsConflictWithSessionId_RetriesWithSessionIdHeader()
    {
        var callCount = 0;
        string secondCallSessionId = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Conflict);
                resp.Headers.Add("X-Transmission-Session-Id", "session-token-12345");
                return resp;
            }

            if (req.Headers.TryGetValues("X-Transmission-Session-Id", out var values))
            {
                secondCallSessionId = string.Join(",", values);
            }

            var json = "{\"arguments\":{\"torrents\":[{\"hashString\":\"4444444444444444444444444444444444444444\",\"name\":\"Retried Torrent\",\"totalSize\":1024,\"percentDone\":0.5,\"downloadDir\":\"/data\"}]}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        callCount.Should().Be(2);
        secondCallSessionId.Should().Be("session-token-12345");
        items.Should().HaveCount(1);
        items[0].InfoHash.Should().Be("4444444444444444444444444444444444444444");
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenDelugeCredentialsProvided_SendsAuthLoginJsonRpc()
    {
        string capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body != null && body.Contains("auth.login"))
            {
                capturedBody = body;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":true,\"error\":null,\"id\":1}", Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":{},\"error\":null,\"id\":1}", Encoding.UTF8, "application/json") };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "Deluge",
            Host = "127.0.0.1",
            Port = 8112,
            Password = "delugepassword",
        };

        await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("auth.login");
        capturedBody.Should().Contain("delugepassword");
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenDelugeLoginReturnsResultFalse_FailsAuthenticationAndReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body != null && body.Contains("auth.login"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":false,\"error\":null,\"id\":1}", Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":{\"abc\":{}},\"error\":null,\"id\":1}", Encoding.UTF8, "application/json") };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "Deluge",
            Host = "127.0.0.1",
            Port = 8112,
            Password = "wrongPassword",
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);
        items.Should().BeEmpty();
    }

    [Test]
    public void DownloadClientPasswordHelper_RoundTrip_ProtectsAndUnprotectsPassword()
    {
        var original = "SuperSecretPassword!@#123";
        var protectedPass = DownloadClientPasswordHelper.Protect(original);

        protectedPass.Should().NotBe(original);
        protectedPass.Should().StartWith("enc:");

        var unprotected = DownloadClientPasswordHelper.Unprotect(protectedPass);
        unprotected.Should().Be(original);
    }

    [Test]
    public void DownloadClientPasswordHelper_Protect_WhenAlreadyProtected_DoesNotDoubleEncrypt()
    {
        var protectedPass = "enc:AlreadyEncryptedBytes==";
        var result = DownloadClientPasswordHelper.Protect(protectedPass);
        result.Should().Be(protectedPass);
    }

    [Test]
    public void DownloadClientPasswordHelper_Unprotect_WhenNotProtected_ReturnsOriginalString()
    {
        var plain = "PlainTextPassword";
        var result = DownloadClientPasswordHelper.Unprotect(plain);
        result.Should().Be(plain);
    }

    [Test]
    public async Task TestDirect_WhenQBittorrentWebapiVersionProbed_ReturnsSuccessWithVersion()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v2/app/webapiVersion"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("2.9.3") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var http = new HttpClient(handler);
        var controller = new DownloadClientController(this.repository, this.torrentService, http);

        var resource = new DownloadClientResource
        {
            Name = "QBitTest",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as DownloadClientTestResult;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Message.Should().Contain("v2.9.3");
    }

    [Test]
    public async Task TestDirect_WhenTransmissionRpcProbed_ReturnsSuccess()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/transmission/rpc"))
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var http = new HttpClient(handler);
        var controller = new DownloadClientController(this.repository, this.torrentService, http);

        var resource = new DownloadClientResource
        {
            Name = "TransTest",
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as DownloadClientTestResult;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Message.Should().Contain("Transmission RPC endpoint reachable");
    }

    [Test]
    public async Task TestDirect_WhenDelugeCheckSessionProbed_ReturnsSuccess()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":true,\"error\":null,\"id\":1}", Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        var controller = new DownloadClientController(this.repository, this.torrentService, http);

        var resource = new DownloadClientResource
        {
            Name = "DelugeTest",
            ClientType = "Deluge",
            Host = "127.0.0.1",
            Port = 8112,
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as DownloadClientTestResult;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Message.Should().Contain("Deluge JSON-RPC connected successfully");
    }

    [Test]
    public async Task TestDirect_WhenTransmissionReturnsUnauthorized_ReturnsFailureMessage()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        var controller = new DownloadClientController(this.repository, this.torrentService, http);

        var resource = new DownloadClientResource
        {
            Name = "TransTestAuthFail",
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
            Username = "baduser",
            Password = "badpassword",
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as DownloadClientTestResult;
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid username or password");
    }

    [Test]
    public async Task TestDirect_WhenHostIsMissing_ReturnsFailedValidation()
    {
        var controller = new DownloadClientController(this.repository, this.torrentService);
        var resource = new DownloadClientResource
        {
            Name = "NoHostClient",
            ClientType = "qBittorrent",
            Host = string.Empty,
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as DownloadClientTestResult;
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Message.Should().Be("Host is required.");
    }

    [Test]
    public async Task TestDirect_WhenUseSslIsTrue_UsesHttpsScheme()
    {
        string probedScheme = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            probedScheme = req.RequestUri!.Scheme;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("2.9.0") };
        });

        using var http = new HttpClient(handler);
        var controller = new DownloadClientController(this.repository, this.torrentService, http);

        var resource = new DownloadClientResource
        {
            Name = "SslClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8443,
            UseSsl = true,
        };

        await controller.TestDirect(resource);
        probedScheme.Should().Be("https");
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenQBittorrentReturnsTorrents_TranslatesAllFieldsCorrectly()
    {
        var json = @"[
            {
                ""hash"": ""5555555555555555555555555555555555555555"",
                ""name"": ""The.Matrix.1999.1080p"",
                ""size"": 4398046511104,
                ""progress"": 0.852,
                ""state"": ""stalledDL"",
                ""save_path"": ""/downloads/movies"",
                ""category"": ""movies""
            }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        items.Should().HaveCount(1);
        var item = items[0];
        item.Id.Should().Be("1");
        item.InfoHash.Should().Be("5555555555555555555555555555555555555555");
        item.Name.Should().Be("The.Matrix.1999.1080p");
        item.Size.Should().Be(4398046511104);
        item.Progress.Should().BeApproximately(0.852, 0.0001);
        item.State.Should().Be("stalledDL");
        item.SavePath.Should().Be("/downloads/movies");
        item.Category.Should().Be("movies");
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenDelugeReturnsTorrents_TranslatesDictionaryAndScalesProgress()
    {
        var json = @"{
            ""result"": {
                ""6666666666666666666666666666666666666666"": {
                    ""name"": ""Frieren.S01E28.1080p"",
                    ""total_size"": 1420000000,
                    ""progress"": 100.0,
                    ""state"": ""Seeding"",
                    ""save_path"": ""/downloads/anime"",
                    ""label"": ""anime""
                }
            },
            ""error"": null,
            ""id"": 1
        }";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "Deluge",
            Host = "127.0.0.1",
            Port = 8112,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);

        items.Should().HaveCount(1);
        var item = items[0];
        item.InfoHash.Should().Be("6666666666666666666666666666666666666666");
        item.Name.Should().Be("Frieren.S01E28.1080p");
        item.Size.Should().Be(1420000000);
        item.Progress.Should().Be(1.0);
        item.State.Should().Be("Seeding");
        item.SavePath.Should().Be("/downloads/anime");
        item.Category.Should().Be("anime");
    }

    [Test]
    public async Task GetAllItems_WhenTorrentsExistInLibrary_CrossReferencesAndSetsIsInLibraryTrue()
    {
        var json = "[{\"hash\":\"7777777777777777777777777777777777777777\",\"name\":\"Existing Torrent\",\"size\":1000,\"progress\":1.0}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 5,
            Name = "PrimaryClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Enable = true,
        };

        this.repository.GetEnabled().Returns(new List<DownloadClientDefinition> { client });

        var existingTorrent = new Torrent
        {
            Id = 42,
            InfoHash = "7777777777777777777777777777777777777777",
            Name = "Existing Torrent",
        };
        this.torrentService.GetByInfoHash("7777777777777777777777777777777777777777").Returns(existingTorrent);

        var controller = new DownloadClientController(this.repository, this.torrentService, http);
        var actionResult = await controller.GetAllItems();
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var list = okResult!.Value as List<DownloadClientRemoteItem>;
        list.Should().NotBeNull();
        list!.Should().HaveCount(1);
        list[0].ClientId.Should().Be(5);
        list[0].ClientName.Should().Be("PrimaryClient");
        list[0].IsInLibrary.Should().BeTrue();
        list[0].LibraryTorrentId.Should().Be(42);
    }

    [Test]
    public async Task GetAllItems_WhenTorrentsDoNotInLibrary_SetsIsInLibraryFalse()
    {
        var json = "[{\"hash\":\"8888888888888888888888888888888888888888\",\"name\":\"New Torrent\",\"size\":2000,\"progress\":0.2}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 6,
            Name = "SecondaryClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Enable = true,
        };

        this.repository.GetEnabled().Returns(new List<DownloadClientDefinition> { client });
        this.torrentService.GetByInfoHash("8888888888888888888888888888888888888888").Returns((Torrent)null!);

        var controller = new DownloadClientController(this.repository, this.torrentService, http);
        var actionResult = await controller.GetAllItems();
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var list = okResult!.Value as List<DownloadClientRemoteItem>;
        list.Should().NotBeNull();
        list!.Should().HaveCount(1);
        list[0].IsInLibrary.Should().BeFalse();
        list[0].LibraryTorrentId.Should().BeNull();
    }

    [Test]
    public async Task ImportTorrent_Single_WhenUntracked_QueriesClientAndPreservesCategoryAndSavePath()
    {
        var hash = "9999999999999999999999999999999999999999";
        var json = $"[{{\"hash\":\"{hash}\",\"name\":\"Target Movie\",\"size\":1500,\"progress\":1.0,\"save_path\":\"/mnt/storage/movies\",\"category\":\"custom-cat\"}}]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 7,
            Name = "ImportSourceClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Category = "default-cat",
        };

        this.repository.Get(7).Returns(client);
        this.torrentService.GetByInfoHash(hash).Returns((Torrent)null!);

        var addedTorrent = new Torrent
        {
            Id = 101,
            InfoHash = hash,
            Name = "Target Movie",
            Category = "custom-cat",
            SavePath = "/mnt/storage/movies",
        };

        this.torrentService.AddFromMagnetAsync(
            $"magnet:?xt=urn:btih:{hash}",
            "custom-cat",
            "/mnt/storage/movies",
            false).Returns(Task.FromResult(addedTorrent));

        var controller = new DownloadClientController(this.repository, this.torrentService, http);
        var actionResult = await controller.ImportTorrent(7, hash);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var resource = okResult!.Value as TorrentResource;
        resource.Should().NotBeNull();
        resource!.InfoHash.Should().Be(hash);
        resource.Category.Should().Be("custom-cat");

        await this.torrentService.Received(1).AddFromMagnetAsync(
            $"magnet:?xt=urn:btih:{hash}",
            "custom-cat",
            "/mnt/storage/movies",
            false);
    }

    [Test]
    public async Task ImportTorrent_Single_WhenAlreadyTracked_ReturnsExistingTorrentWithoutCallingAdd()
    {
        var hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var client = new DownloadClientDefinition { Id = 8, Name = "Client8" };
        this.repository.Get(8).Returns(client);

        var existing = new Torrent
        {
            Id = 102,
            InfoHash = hash,
            Name = "Already Tracked",
        };
        this.torrentService.GetByInfoHash(hash).Returns(existing);

        var controller = new DownloadClientController(this.repository, this.torrentService);
        var actionResult = await controller.ImportTorrent(8, hash);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        await this.torrentService.DidNotReceive().AddFromMagnetAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>());
    }

    [Test]
    public async Task ImportTorrent_Single_WhenClientNotFound_ReturnsNotFound()
    {
        this.repository.Get(999).Returns((DownloadClientDefinition)null!);

        var controller = new DownloadClientController(this.repository, this.torrentService);
        var actionResult = await controller.ImportTorrent(999, "anyhash");

        actionResult.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task ImportTorrents_Batch_WhenMultipleHashes_ImportsUntrackedAndSkipsTracked()
    {
        var hash1 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var hash2 = "cccccccccccccccccccccccccccccccccccccccc";

        var json = $"[{{\"hash\":\"{hash1}\",\"name\":\"Torrent 1\",\"save_path\":\"/path1\",\"category\":\"cat1\"}},{{\"hash\":\"{hash2}\",\"name\":\"Torrent 2\",\"save_path\":\"/path2\",\"category\":\"cat2\"}}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 9,
            Name = "BatchClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
        };

        this.repository.Get(9).Returns(client);

        // hash1 is not in library, hash2 is already in library
        this.torrentService.GetByInfoHash(hash1).Returns((Torrent)null!);
        this.torrentService.GetByInfoHash(hash2).Returns(new Torrent { Id = 200, InfoHash = hash2 });

        this.torrentService.AddFromMagnetAsync(
            $"magnet:?xt=urn:btih:{hash1}",
            "cat1",
            "/path1",
            false).Returns(Task.FromResult(new Torrent { Id = 201, InfoHash = hash1 }));

        var controller = new DownloadClientController(this.repository, this.torrentService, http);
        var request = new ImportRequest
        {
            Hashes = new List<string> { hash1, hash2 },
        };

        var actionResult = await controller.ImportTorrents(9, request);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var syncResult = okResult!.Value as SyncResultResource;
        syncResult.Should().NotBeNull();
        syncResult!.Success.Should().BeTrue();
        syncResult.SyncedCount.Should().Be(1);
        syncResult.Added.Should().Be(1);
        syncResult.Skipped.Should().Be(1);
        syncResult.TotalCount.Should().Be(2);

        await this.torrentService.Received(1).AddFromMagnetAsync(
            $"magnet:?xt=urn:btih:{hash1}",
            "cat1",
            "/path1",
            false);

        await this.torrentService.DidNotReceive().AddFromMagnetAsync(
            $"magnet:?xt=urn:btih:{hash2}",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>());
    }

    [Test]
    public async Task ImportTorrents_Batch_WhenEmptyHashesProvided_ReturnsBadRequest()
    {
        var client = new DownloadClientDefinition { Id = 10, Name = "BatchClient" };
        this.repository.Get(10).Returns(client);

        var controller = new DownloadClientController(this.repository, this.torrentService);
        var actionResult = await controller.ImportTorrents(10, new ImportRequest { Hashes = new List<string>() });

        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task ImportTorrents_Batch_WhenOneImportThrowsException_ContinuesProcessingRemainingTorrents()
    {
        var hash1 = "dddddddddddddddddddddddddddddddddddddddd";
        var hash2 = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        var json = $"[{{\"hash\":\"{hash1}\",\"name\":\"Failing Torrent\"}},{{\"hash\":\"{hash2}\",\"name\":\"Succeeding Torrent\"}}]";
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 11,
            Name = "ResilientClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Category = "default-cat",
        };

        this.repository.Get(11).Returns(client);
        this.torrentService.GetByInfoHash(Arg.Any<string>()).Returns((Torrent)null!);

        this.torrentService.AddFromMagnetAsync($"magnet:?xt=urn:btih:{hash1}", Arg.Any<string>(), Arg.Any<string>(), false)
            .Throws(new InvalidOperationException("Torrent engine failure on hash1"));

        this.torrentService.AddFromMagnetAsync($"magnet:?xt=urn:btih:{hash2}", Arg.Any<string>(), Arg.Any<string>(), false)
            .Returns(Task.FromResult(new Torrent { Id = 202, InfoHash = hash2 }));

        var controller = new DownloadClientController(this.repository, this.torrentService, http);
        var request = new ImportRequest { Hashes = new List<string> { hash1, hash2 } };

        var actionResult = await controller.ImportTorrents(11, request);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var syncResult = okResult!.Value as SyncResultResource;
        syncResult.Should().NotBeNull();
        syncResult!.SyncedCount.Should().Be(1);
        syncResult.Skipped.Should().Be(1);
    }

    [Test]
    public async Task Sync_WhenOneClientFails_ContinuesRemainingClientsAndIncrementsFailedCount()
    {
        var goodClient = new DownloadClientDefinition
        {
            Id = 1,
            Name = "GoodClient",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Enable = true,
        };

        var badClient = new DownloadClientDefinition
        {
            Id = 2,
            Name = "BadClient",
            ClientType = "qBittorrent",
            Host = "10.255.255.1",
            Port = 8080,
            Enable = true,
        };

        this.repository.GetEnabled().Returns(new List<DownloadClientDefinition> { badClient, goodClient });

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host == "10.255.255.1")
            {
                throw new HttpRequestException("Host unreachable");
            }

            var json = "[{\"hash\":\"ffffffffffffffffffffffffffffffffffffffff\",\"name\":\"Sync Torrent\"}]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });

        using var http = new HttpClient(handler);
        this.torrentService.GetByInfoHash("ffffffffffffffffffffffffffffffffffffffff").Returns((Torrent)null!);
        this.torrentService.AddFromMagnetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), false)
            .Returns(Task.FromResult(new Torrent { Id = 300, InfoHash = "ffffffffffffffffffffffffffffffffffffffff" }));

        var controller = new DownloadClientSyncController(this.repository, this.torrentService, http);
        var actionResult = await controller.Sync();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Failed.Should().Be(0);
        result.SyncedCount.Should().Be(1);
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenHostIsLinkLocalAddress_BlocksSsrfAndReturnsEmpty()
    {
        var client = new DownloadClientDefinition
        {
            ClientType = "qBittorrent",
            Host = "169.254.169.254",
            Port = 80,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client);
        items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenSafeHttpClientServiceThrows_BlocksQueryAndReturnsEmpty()
    {
        this.safeHttpClientService.When(s => s.ValidateUrl(Arg.Any<string>()))
            .Do(_ => throw new SecurityException("SSRF blocked destination"));

        var client = new DownloadClientDefinition
        {
            ClientType = "qBittorrent",
            Host = "internal-service.local",
            Port = 8080,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, null, this.safeHttpClientService);
        items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenClientIsNull_ReturnsEmptyListImmediately()
    {
        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(null!);
        items.Should().NotBeNull();
        items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenResponseContainsMalformedJson_HandlesGracefullyWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<!DOCTYPE html><html><body>Error 500 Invalid Response</body></html>", Encoding.UTF8, "text/html"),
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
        };

        var act = async () => await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);
        await act.Should().NotThrowAsync();

        var items = await act();
        items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryRemoteClientItemsAsync_WhenHttp500Returned_ReturnsEmptyListWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            ClientType = "Transmission",
            Host = "127.0.0.1",
            Port = 9091,
        };

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http);
        items.Should().BeEmpty();
    }

    [TestCase(-1)]
    [TestCase(65536)]
    public void Create_WhenPortIsOutOfRange_ReturnsBadRequest(int invalidPort)
    {
        var controller = new DownloadClientController(this.repository, this.torrentService);
        var resource = new DownloadClientResource
        {
            Name = "InvalidPortClient",
            Host = "127.0.0.1",
            Port = invalidPort,
        };

        var result = controller.Create(resource);
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(null)]
    public void Create_WhenNameIsNullOrWhiteSpace_ReturnsBadRequest(string invalidName)
    {
        var controller = new DownloadClientController(this.repository, this.torrentService);
        var resource = new DownloadClientResource
        {
            Name = invalidName,
            Host = "127.0.0.1",
            Port = 8080,
        };

        var result = controller.Create(resource);
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Create_WhenResourceIsNull_ReturnsBadRequest()
    {
        var controller = new DownloadClientController(this.repository, this.torrentService);
        var result = controller.Create(null!);
        result.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public void Update_WhenResourceIsNull_ReturnsBadRequest()
    {
        var controller = new DownloadClientController(this.repository, this.torrentService);
        var result = controller.Update(1, null!);
        result.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public async Task TestDirect_WhenResourceIsNull_ReturnsBadRequest()
    {
        var controller = new DownloadClientController(this.repository, this.torrentService);
        var result = await controller.TestDirect(null!);
        result.Result.Should().BeOfType<BadRequestResult>();
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
