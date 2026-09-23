// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Hadouken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class HadoukenRpcControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private HadoukenRpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new HadoukenRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.torrentFileService,
            this.configFileProvider,
            this.safeHttpClientService);

        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [TestCase("torrents.get_files")]
    [TestCase("webui.getfiles")]
    public async Task GetFiles_EnrichesFileProgress(string method)
    {
        var infoHash = "abc123456789abcdef0123456789abcdef012345";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = infoHash,
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 1000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 10, TorrentId = 1, Path = "file1.mkv", Size = 1000, Progress = 0.0 },
        };

        this.torrentService.GetByInfoHash(infoHash).Returns(torrent);
        this.torrentFileService.GetFiles(1).Returns(files);

        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns(new bool[] { true, false });
        this.torrentService.GetDownloadTask(1).Returns(downloadTask);

        var requestJson = $"{{\"method\":\"{method}\",\"params\":[\"{infoHash}\"],\"id\":1}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = method,
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        };

        var actionResult = await this.controller.HandleRpc(request);
        actionResult.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)actionResult;
        okResult.Value.Should().NotBeNull();

        // Before enrichment progress was 0.0, enricher sets it to torrent.Progress (0.5)
        files[0].Progress.Should().Be(0.5);
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("\"true\"", true)]
    [TestCase("\"false\"", false)]
    [TestCase("\"1\"", true)]
    [TestCase("\"0\"", false)]
    [TestCase("null", false)]
    public async Task HandleRpc_TorrentsAddUri_WithVariousBooleanFormatsForPaused_DoesNotThrowAndParsesCorrectly(string pausedJson, bool expectedPaused)
    {
        var magnetUri = "magnet:?xt=urn:btih:1234567890123456789012345678901234567890&dn=Test";
        var added = new Torrent
        {
            Id = 1,
            InfoHash = "1234567890123456789012345678901234567890",
            Name = "Test",
        };
        this.torrentService.AddFromMagnetAsync(magnetUri, null, null, expectedPaused).Returns(added);

        var requestJson = $"{{\"method\":\"torrents.adduri\",\"params\":[\"{magnetUri}\",{{\"paused\":{pausedJson}}}],\"id\":1}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "torrents.adduri",
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        };

        var actionResult = await this.controller.HandleRpc(request);
        actionResult.Should().BeOfType<OkObjectResult>();

        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, null, null, expectedPaused);
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("\"true\"", true)]
    [TestCase("\"false\"", false)]
    [TestCase("\"1\"", true)]
    [TestCase("\"0\"", false)]
    [TestCase("null", false)]
    public async Task HandleRpc_TorrentsDelete_WithVariousBooleanFormatsForDeleteData_DoesNotThrowAndParsesCorrectly(string deleteDataJson, bool expectedDelete)
    {
        var infoHash = "1234567890123456789012345678901234567890";
        var torrent = new Torrent { Id = 7, InfoHash = infoHash };
        this.torrentService.GetByInfoHash(infoHash).Returns(torrent);

        var requestJson = $"{{\"method\":\"torrents.delete\",\"params\":[\"{infoHash}\",{deleteDataJson}],\"id\":1}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "torrents.delete",
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        };

        var actionResult = await this.controller.HandleRpc(request);
        actionResult.Should().BeOfType<OkObjectResult>();

        await this.torrentService.Received(1).DeleteAsync(7, expectedDelete);
    }

    [Test]
    public async Task HandleRpc_NullOrEmptyRequest_ReturnsInvalidRequestError()
    {
        var nullResult = await this.controller.HandleRpc(null!);
        nullResult.Should().BeOfType<OkObjectResult>();
        var okNull = (OkObjectResult)nullResult;
        var jsonNull = JsonSerializer.Serialize(okNull.Value);
        using (var docNull = JsonDocument.Parse(jsonNull))
        {
            docNull.RootElement.GetProperty("error").GetString().Should().Be("Invalid request");
        }

        var emptyMethodRequest = new HadoukenRpcRequest { Method = "   ", Id = 99 };
        var emptyResult = await this.controller.HandleRpc(emptyMethodRequest);
        emptyResult.Should().BeOfType<OkObjectResult>();
        var okEmpty = (OkObjectResult)emptyResult;
        var jsonEmpty = JsonSerializer.Serialize(okEmpty.Value);
        using (var docEmpty = JsonDocument.Parse(jsonEmpty))
        {
            docEmpty.RootElement.GetProperty("error").GetString().Should().Be("Invalid request");
        }
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WithValidCredentials_ReturnsSessionToken()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_hadouken_key");

        var requestJson = "{\"method\":\"auth.login\",\"params\":[\"secret_hadouken_key\"],\"id\":1}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "auth.login",
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        };

        var result = await this.controller.HandleRpc(request);
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        var token = resDoc.RootElement.GetProperty("result").GetString();
        token.Should().NotBeNullOrWhiteSpace();

        // Now verify using this token in header authenticates subsequent call
        var authContext = new DefaultHttpContext();
        authContext.Request.Headers["X-Hadouken-Token"] = token;
        this.controller.ControllerContext = new ControllerContext { HttpContext = authContext };

        var versionRequest = new HadoukenRpcRequest { Method = "core.getversion", Id = 2 };
        var versionResult = await this.controller.HandleRpc(versionRequest);
        versionResult.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WithInvalidCredentials_ReturnsError()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("correct_key");

        var requestJson = "{\"method\":\"auth.login\",\"params\":[\"wrong_key\"],\"id\":1}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "auth.login",
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        };

        var result = await this.controller.HandleRpc(request);
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("error").GetString().Should().Be("Invalid credentials");
    }

    [Test]
    public async Task HandleRpc_WhenAuthEnabledAndNoToken_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("super_secret");

        var request = new HadoukenRpcRequest { Method = "core.getversion", Id = 5 };
        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task HandleRpc_WhenAuthEnabledAndQueryTokenProvided_Succeeds()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("query_test_key");

        var loginJson = "{\"method\":\"auth.generate_token\",\"params\":[\"query_test_key\"],\"id\":1}";
        using var loginDoc = JsonDocument.Parse(loginJson);
        var loginReq = new HadoukenRpcRequest
        {
            Method = "auth.generate_token",
            Params = loginDoc.RootElement.GetProperty("params"),
            Id = 1,
        };
        var loginResult = (OkObjectResult)await this.controller.HandleRpc(loginReq);
        var loginResJson = JsonSerializer.Serialize(loginResult.Value);
        using var loginResDoc = JsonDocument.Parse(loginResJson);
        var token = loginResDoc.RootElement.GetProperty("result").GetString();

        var queryContext = new DefaultHttpContext();
        queryContext.Request.QueryString = new QueryString($"?token={token}");
        this.controller.ControllerContext = new ControllerContext { HttpContext = queryContext };

        var versionReq = new HadoukenRpcRequest { Method = "core.getversion", Id = 2 };
        var versionResult = await this.controller.HandleRpc(versionReq);
        versionResult.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task HandleRpc_AuthLogout_RemovesSessionToken()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("logout_key");

        var loginJson = "{\"method\":\"auth.login\",\"params\":[\"logout_key\"],\"id\":1}";
        using var loginDoc = JsonDocument.Parse(loginJson);
        var loginReq = new HadoukenRpcRequest
        {
            Method = "auth.login",
            Params = loginDoc.RootElement.GetProperty("params"),
            Id = 1,
        };
        var loginResult = (OkObjectResult)await this.controller.HandleRpc(loginReq);
        var loginResJson = JsonSerializer.Serialize(loginResult.Value);
        using var loginResDoc = JsonDocument.Parse(loginResJson);
        var token = loginResDoc.RootElement.GetProperty("result").GetString();

        var authContext = new DefaultHttpContext();
        authContext.Request.Headers["X-Hadouken-Token"] = token;
        this.controller.ControllerContext = new ControllerContext { HttpContext = authContext };

        var logoutReq = new HadoukenRpcRequest { Method = "auth.logout", Id = 2 };
        var logoutResult = await this.controller.HandleRpc(logoutReq);
        logoutResult.Should().BeOfType<OkObjectResult>();

        // Next call with same token should now be unauthorized
        var nextReq = new HadoukenRpcRequest { Method = "core.getversion", Id = 3 };
        var nextResult = await this.controller.HandleRpc(nextReq);
        nextResult.Should().BeOfType<ObjectResult>();
        ((ObjectResult)nextResult).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task HandleRpc_CoreGetSystemInfo_ReturnsExpectedInfo()
    {
        var request = new HadoukenRpcRequest { Method = "core.getsysteminfo", Id = 10 };
        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result").GetProperty("committish").GetString().Should().Be("5.3.0");
        doc.RootElement.GetProperty("result").GetProperty("branch").GetString().Should().Be("master");
    }

    [Test]
    public async Task HandleRpc_WebUiGetSettings_ReturnsDownloadDirectory()
    {
        this.configService.DownloadDir.Returns("/custom/hadouken/downloads");

        var request = new HadoukenRpcRequest { Method = "webui.getsettings", Id = 11 };
        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result").GetProperty("bittorrent.default_save_path").GetString().Should().Be("/custom/hadouken/downloads");
    }

    [Test]
    public async Task HandleRpc_WebUiList_ReturnsMappedTorrentsWithStatusFlags()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, InfoHash = "aaaa111122223333444455556666777788889999", Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.4, TotalSize = 1000 },
            new() { Id = 2, InfoHash = "bbbb111122223333444455556666777788889999", Name = "T2", Status = TorrentStatus.Seeding, Progress = 1.0, TotalSize = 2000 },
            new() { Id = 3, InfoHash = "cccc111122223333444455556666777788889999", Name = "T3", Status = TorrentStatus.Paused, Progress = 0.2, TotalSize = 3000 },
            new() { Id = 4, InfoHash = "dddd111122223333444455556666777788889999", Name = "T4", Status = TorrentStatus.Stopped, Progress = 0.1, TotalSize = 4000 },
            new() { Id = 5, InfoHash = "eeee111122223333444455556666777788889999", Name = "T5", Status = TorrentStatus.Checking, Progress = 0.5, TotalSize = 5000 },
            new() { Id = 6, InfoHash = "ffff111122223333444455556666777788889999", Name = "T6", Status = TorrentStatus.Error, Progress = 0.0, TotalSize = 6000 },
            new() { Id = 7, InfoHash = "1111222233334444555566667777888899990000", Name = "T7", Status = TorrentStatus.Queued, Progress = 0.0, TotalSize = 7000 },
        };
        this.torrentService.GetAll().Returns(torrents);

        var request = new HadoukenRpcRequest { Method = "webui.list", Id = 12 };
        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("result").GetProperty("torrents");
        rows.GetArrayLength().Should().Be(7);
    }

    [Test]
    public async Task HandleRpc_WebUiAddTorrent_FileType_AddsParsedTorrent()
    {
        var rawBytes = new byte[] { 1, 2, 3, 4 };
        var base64 = Convert.ToBase64String(rawBytes);
        var parsed = new ParsedTorrent { InfoHash = "feedbeef11223344556677889900112233445566", Name = "AddFile.Torrent" };
        var added = new Torrent { Id = 20, InfoHash = parsed.InfoHash, Name = parsed.Name };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, "movies", "/custom/save", true, Arg.Any<byte[]>()).Returns(added);

        var requestJson = $"{{\"method\":\"webui.addtorrent\",\"params\":[\"file\",\"{base64}\",{{\"save_path\":\"/custom/save\",\"label\":\"movies\",\"paused\":true}}],\"id\":21}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "webui.addtorrent",
            Params = doc.RootElement.GetProperty("params"),
            Id = 21,
        };

        var result = await this.controller.HandleRpc(request);
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetString().Should().Be(parsed.InfoHash);
    }

    [Test]
    public async Task HandleRpc_WebUiAddTorrent_UrlType_AddsMagnet()
    {
        var magnetUri = "magnet:?xt=urn:btih:feedbeef11223344556677889900112233445566&dn=Test";
        var added = new Torrent { Id = 22, InfoHash = "feedbeef11223344556677889900112233445566" };
        this.torrentService.AddFromMagnetAsync(magnetUri, "anime", null, false).Returns(added);

        var requestJson = $"{{\"method\":\"webui.addtorrent\",\"params\":[\"url\",\"{magnetUri}\",{{\"tags\":[\"anime\"],\"paused\":false}}],\"id\":23}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "webui.addtorrent",
            Params = doc.RootElement.GetProperty("params"),
            Id = 23,
        };

        var result = await this.controller.HandleRpc(request);
        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, "anime", null, false);
    }

    [TestCase("pause")]
    [TestCase("resume")]
    [TestCase("start")]
    [TestCase("recheck")]
    [TestCase("remove")]
    [TestCase("removedata")]
    public async Task HandleRpc_WebUiPerform_WithVariousActions_ExecutesExpectedOperations(string action)
    {
        var hash = "99887766554433221100aabbccddeeff00112233";
        var torrent = new Torrent { Id = 35, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var requestJson = $"{{\"method\":\"webui.perform\",\"params\":[\"{action}\",[\"{hash}\"]],\"id\":30}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "webui.perform",
            Params = doc.RootElement.GetProperty("params"),
            Id = 30,
        };

        var result = await this.controller.HandleRpc(request);
        result.Should().BeOfType<OkObjectResult>();

        switch (action)
        {
            case "pause":
                await this.torrentService.Received(1).PauseAsync(35);
                break;
            case "resume":
            case "start":
                await this.torrentService.Received(1).ResumeAsync(35);
                break;
            case "recheck":
                await this.torrentService.Received(1).ForceRecheckAsync(35);
                break;
            case "remove":
                await this.torrentService.Received(1).DeleteAsync(35, false);
                break;
            case "removedata":
                await this.torrentService.Received(1).DeleteAsync(35, true);
                break;
        }
    }

    [Test]
    public async Task HandleRpc_TorrentsPauseAndResume_CallsTorrentService()
    {
        var hash = "aabb11223344556677889900aabbccddeeff0011";
        var torrent = new Torrent { Id = 40, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var pauseJson = $"{{\"method\":\"torrents.pause\",\"params\":[\"{hash}\"],\"id\":41}}";
        using var pauseDoc = JsonDocument.Parse(pauseJson);
        var pauseReq = new HadoukenRpcRequest
        {
            Method = "torrents.pause",
            Params = pauseDoc.RootElement.GetProperty("params"),
            Id = 41,
        };
        await this.controller.HandleRpc(pauseReq);
        await this.torrentService.Received(1).PauseAsync(40);

        var resumeJson = $"{{\"method\":\"torrents.resume\",\"params\":[\"{hash}\"],\"id\":42}}";
        using var resumeDoc = JsonDocument.Parse(resumeJson);
        var resumeReq = new HadoukenRpcRequest
        {
            Method = "torrents.resume",
            Params = resumeDoc.RootElement.GetProperty("params"),
            Id = 42,
        };
        await this.controller.HandleRpc(resumeReq);
        await this.torrentService.Received(1).ResumeAsync(40);
    }

    [Test]
    public async Task HandleRpc_TorrentsSetProps_UpdatesTorrentProperties()
    {
        var hash = "ccdd11223344556677889900aabbccddeeff0011";
        var torrent = new Torrent { Id = 50, InfoHash = hash, Category = "old", DownloadLimit = 0, UploadLimit = 0 };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var requestJson = $"{{\"method\":\"torrents.set_props\",\"params\":[\"{hash}\",{{\"tags\":[\"updated_tag\"],\"download_limit\":1024,\"upload_limit\":512}}],\"id\":51}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "torrents.set_props",
            Params = doc.RootElement.GetProperty("params"),
            Id = 51,
        };

        var result = await this.controller.HandleRpc(request);
        result.Should().BeOfType<OkObjectResult>();

        torrent.Category.Should().Be("updated_tag");
        torrent.DownloadLimit.Should().Be(1024);
        torrent.UploadLimit.Should().Be(512);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [TestCase("session.get_torrents")]
    [TestCase("torrents.add")]
    [TestCase("torrents.remove")]
    [TestCase("unknown.method.name")]
    public async Task HandleRpc_UnhandledMethods_ReturnSuccessResult(string unhandledMethod)
    {
        var request = new HadoukenRpcRequest { Method = unhandledMethod, Id = 99 };
        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task HandleRpc_WhenExceptionOccurs_ReturnsErrorObject()
    {
        var hash = "errorhash00000000000000000000000000000000";
        this.torrentService.GetByInfoHash(hash).Throws(new InvalidOperationException("Torrent service fatal error"));

        var requestJson = $"{{\"method\":\"torrents.pause\",\"params\":[\"{hash}\"],\"id\":77}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = "torrents.pause",
            Params = doc.RootElement.GetProperty("params"),
            Id = 77,
        };

        var result = await this.controller.HandleRpc(request);
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("error").GetString().Should().Be("Torrent service fatal error");
    }

    [Test]
    public async Task HandleRpc_SimulatedBatchRpc_ProcessesSequenceOfRequests()
    {
        var requests = new List<HadoukenRpcRequest>
        {
            new() { Method = "core.getversion", Id = 1 },
            new() { Method = "core.getsysteminfo", Id = 2 },
            new() { Method = "webui.getsettings", Id = 3 },
        };

        var responses = new List<IActionResult>();
        foreach (var req in requests)
        {
            var res = await this.controller.HandleRpc(req);
            responses.Add(res);
        }

        responses.Should().HaveCount(3);
        responses.Should().AllBeOfType<OkObjectResult>();
    }
}
