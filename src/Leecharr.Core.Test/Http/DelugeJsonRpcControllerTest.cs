// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Deluge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class DelugeJsonRpcControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IDiskProvider diskProvider = null!;
    private DelugeJsonRpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.diskProvider = Substitute.For<IDiskProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("deluge_secret_key");

        this.controller = new DelugeJsonRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            diskProvider: this.diskProvider);
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WithWrongPassword_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"auth.login\",\"params\":[\"wrong_password\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":false");
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WithCorrectPassword_ReturnsTrueAndSetsCookie()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"auth.login\",\"params\":[\"deluge_secret_key\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        context.Response.Headers.ContainsKey("Set-Cookie").Should().BeTrue();
        context.Response.Headers["Set-Cookie"].ToString().Should().Contain("deluge-session");
    }

    [Test]
    public async Task HandleRpc_ManagementMethod_WhenUnauthenticated_Returns401()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task HandleRpc_ManagementMethod_WithValidApiKeyHeader_Succeeds()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"error\":null");
        json.Should().Contain("\"result\":");
    }

    [Test]
    public async Task HandleRpc_GetTorrentsStatus_WithoutFilesKey_DoesNotQueryFileService()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new System.Collections.Generic.List<Torrent> { torrent });

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{}, [\"name\", \"state\", \"progress\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        this.torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
    }

    [Test]
    public async Task HandleRpc_GetTorrentsStatus_WithFilesKey_QueriesFileService()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new System.Collections.Generic.List<Torrent> { torrent });
        this.torrentFileService.GetFiles(42).Returns(new System.Collections.Generic.List<TorrentFile>());

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{}, [\"name\", \"files\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        this.torrentFileService.Received(1).GetFiles(42);
    }

    [Test]
    public async Task HandleRpc_CoreMoveStorage_WithArrayHashes_InvokesSetLocationAsyncWithMoveTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        using var doc = JsonDocument.Parse("{\"method\":\"core.move_storage\",\"params\":[[\"aabbccddeeff00112233445566778899aabbccdd\"], \"/downloads/new_dest\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/new_dest", moveFiles: true);
    }

    [Test]
    public async Task HandleRpc_CoreMoveStorage_WithStringHash_InvokesSetLocationAsyncWithMoveTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        using var doc = JsonDocument.Parse("{\"method\":\"core.move_storage\",\"params\":[\"aabbccddeeff00112233445566778899aabbccdd\", \"/downloads/new_dest\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/new_dest", moveFiles: true);
    }

    [Test]
    public async Task HandleRpc_CoreSetTorrentOptions_WithDownloadLocation_InvokesSetLocationAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        using var doc = JsonDocument.Parse("{\"method\":\"core.set_torrent_options\",\"params\":[[\"aabbccddeeff00112233445566778899aabbccdd\"], {\"download_location\": \"/downloads/relocated\"}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/relocated", moveFiles: true);
        await this.torrentService.DidNotReceive().UpdateAsync(Arg.Any<Torrent>());
    }

    [Test]
    public async Task HandleRpc_CoreSetTorrentOptions_WithMoveCompletedPath_InvokesSetLocationAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        using var doc = JsonDocument.Parse("{\"method\":\"core.set_torrent_options\",\"params\":[[\"aabbccddeeff00112233445566778899aabbccdd\"], {\"move_completed_path\": \"/downloads/completed\"}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/completed", moveFiles: true);
        await this.torrentService.DidNotReceive().UpdateAsync(Arg.Any<Torrent>());
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentStatus_WithFiles_ReturnsEnrichedFileProgress()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Deluge.Test",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            PieceLength = 500,
            PieceCount = 4,
            TotalSize = 2000,
        };

        var task = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadTask>();
        task.PieceBitfield.Returns(new[] { true, true, false, false });
        task.PieceLength.Returns(500);

        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 42, Path = "file1.bin", Size = 1000, PieceOffset = 0, PieceCount = 2 },
            new() { Id = 2, TorrentId = 42, Path = "file2.bin", Size = 1000, PieceOffset = 2, PieceCount = 2 },
        };

        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);
        this.torrentService.GetDownloadTask(42).Returns(task);
        this.torrentFileService.GetFiles(42).Returns(files);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrent_status\",\"params\":[\"aabbccddeeff00112233445566778899aabbccdd\", [\"files\", \"file_progress\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"file_progress\":[1,0]");
    }

    [Test]
    public async Task HandleRpc_BatchedCalls_ReturnsArrayOfResponses()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("[{\"method\":\"web.connected\",\"params\":[],\"id\":1},{\"method\":\"daemon.info\",\"params\":[],\"id\":2}]");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"id\":1");
        json.Should().Contain("\"id\":2");
        json.Should().StartWith("[");
    }

    [Test]
    public async Task HandleRpc_CoreGetConfigValues_ReturnsRequestedKeys()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/custom/downloads");
        this.configService.MaxGlobalConnections.Returns(200);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_config_values\",\"params\":[[\"download_location\",\"max_connections_global\",\"move_completed\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"download_location\":\"/custom/downloads\"");
        json.Should().Contain("\"max_connections_global\":200");
        json.Should().Contain("\"move_completed\":false");
    }

    [Test]
    public async Task HandleRpc_UnhandledMethod_ReturnsJsonRpcError()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"nonexistent.method\",\"params\":[],\"id\":99}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":null");
        json.Should().Contain("\"message\":\"Unknown method: nonexistent.method\"");
    }

    [Test]
    public async Task HandleRpc_CoreGetFreeSpace_WithEmptyParam_UsesConfigDownloadDir()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(500_000_000_000L);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_free_space\",\"params\":[\"\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":500000000000");
        json.Should().Contain("\"error\":null");
    }

    [Test]
    public async Task HandleRpc_CoreGetFreeSpace_WithCustomPath_QueriesDiskProvider()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(250_000_000_000L);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_free_space\",\"params\":[\"/mnt/storage\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":250000000000");
    }

    [Test]
    public async Task HandleRpc_SetTorrentOptions_WithRateLimits_SetsLimitsInKbpsWithoutMultiplier()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            DownloadLimit = 0,
            UploadLimit = 0,
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        using var doc = JsonDocument.Parse("{\"method\":\"core.set_torrent_options\",\"params\":[[\"aabbccddeeff00112233445566778899aabbccdd\"], {\"max_download_speed\": 500, \"max_upload_speed\": 250}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        torrent.DownloadLimit.Should().Be(500);
        torrent.UploadLimit.Should().Be(250);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleRpc_GetTorrentsStatus_ReturnsMaxDownloadAndUploadSpeed()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            DownloadLimit = 500,
            UploadLimit = 250,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{}, [\"name\", \"max_download_speed\", \"max_upload_speed\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"max_download_speed\":500");
        json.Should().Contain("\"max_upload_speed\":250");
    }

    [Test]
    public async Task HandleRpc_GetFilterTree_CoreAndWeb_ReturnsAllStatesAndTrackerHosts()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "1111111111111111111111111111111111111111", Status = TorrentStatus.Downloading, TrackerUrl = "http://tracker1.org:80/announce" },
            new Torrent { Id = 2, InfoHash = "2222222222222222222222222222222222222222", Status = TorrentStatus.Checking, TrackerUrl = "http://tracker1.org:80/announce" },
            new Torrent { Id = 3, InfoHash = "3333333333333333333333333333333333333333", Status = TorrentStatus.Queued, TrackerUrl = "http://tracker2.com:1337/announce" },
            new Torrent { Id = 4, InfoHash = "4444444444444444444444444444444444444444", Status = TorrentStatus.Error, TrackerUrl = "udp://tracker3.org:6969/announce" },
        };
        this.torrentService.GetAll().Returns(torrents);

        // Test web.get_filter_tree
        using var webDoc = JsonDocument.Parse("{\"method\":\"web.get_filter_tree\",\"params\":[],\"id\":1}");
        var webResult = await this.controller.HandleRpc(webDoc.RootElement);

        webResult.Should().BeOfType<JsonResult>();
        var webJsonResult = (JsonResult)webResult;
        var webJson = JsonSerializer.Serialize(webJsonResult.Value);
        webJson.Should().Contain("\"Checking\",1");
        webJson.Should().Contain("\"Queued\",1");
        webJson.Should().Contain("\"Error\",1");
        webJson.Should().Contain("\"tracker_host\"");
        webJson.Should().Contain("\"tracker1.org\",2");
        webJson.Should().Contain("\"tracker2.com\",1");

        // Test core.get_filter_tree
        using var coreDoc = JsonDocument.Parse("{\"method\":\"core.get_filter_tree\",\"params\":[],\"id\":2}");
        var coreResult = await this.controller.HandleRpc(coreDoc.RootElement);

        coreResult.Should().BeOfType<JsonResult>();
        var coreJsonResult = (JsonResult)coreResult;
        var coreJson = JsonSerializer.Serialize(coreJsonResult.Value);
        coreJson.Should().Contain("\"Checking\",1");
        coreJson.Should().Contain("\"Queued\",1");
        coreJson.Should().Contain("\"Error\",1");
        coreJson.Should().Contain("\"tracker_host\"");
    }

    [Test]
    public async Task HandleRpc_WebUpdateUi_IncludesCompleteFilterTree()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "1111111111111111111111111111111111111111", Status = TorrentStatus.Checking, TrackerUrl = "http://tracker1.org/announce" },
        };
        this.torrentService.GetAll().Returns(torrents);

        using var doc = JsonDocument.Parse("{\"method\":\"web.update_ui\",\"params\":[[\"name\"], {}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"Checking\",1");
        json.Should().Contain("\"tracker_host\"");
        json.Should().Contain("\"tracker1.org\",1");
    }

    [Test]
    public async Task HandleRpc_CoreSetConfig_UpdatesConfigServiceValues()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"core.set_config\",\"params\":[{\"max_download_speed\": 500.0, \"max_upload_speed\": 250.0, \"download_location\": \"/mnt/torrents\", \"max_connections_global\": 150}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["MaxDownloadSpeedKbps"] == 500 &&
            (int)d["MaxUploadSpeedKbps"] == 250 &&
            (string)d["DownloadDir"] == "/mnt/torrents" &&
            (int)d["MaxGlobalConnections"] == 150));
    }

    [Test]
    public async Task HandleRpc_WebSetConfig_UpdatesConfigServiceValues()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"web.set_config\",\"params\":[{\"max_active_limit\": 12}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["MaxActiveDownloads"] == 12));
    }

    [Test]
    public async Task HandleRpc_WebUploadTorrent_WithValidBase64_SavesTempFileAndReturnsPath()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var testBytes = new byte[] { 0x64, 0x31, 0x30, 0x3a };
        var base64 = System.Convert.ToBase64String(testBytes);

        using var doc = JsonDocument.Parse($"{{\"method\":\"web.upload_torrent\",\"params\":[\"test.torrent\", \"{base64}\"],\"id\":1}}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":");

        using var resultDoc = JsonDocument.Parse(json);
        var path = resultDoc.RootElement.GetProperty("result").GetString();
        path.Should().NotBeNullOrEmpty();
        System.IO.File.Exists(path).Should().BeTrue();
        var written = await System.IO.File.ReadAllBytesAsync(path!);
        written.Should().Equal(testBytes);

        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public async Task HandleRpc_WebGetTorrentInfo_ReturnsParsedTorrentMetadata()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"deluge_info_test_{System.Guid.NewGuid():N}.torrent");
        var dummyBytes = new byte[] { 1, 2, 3, 4 };
        await System.IO.File.WriteAllBytesAsync(tempPath, dummyBytes);

        var parsed = new ParsedTorrent
        {
            InfoHash = "abcdef1234567890abcdef1234567890abcdef12",
            Name = "Test Torrent",
            TotalSize = 1048576,
            Comment = "Deluge test comment",
            IsPrivate = false,
            Files = new List<ParsedTorrentFile>
            {
                new ParsedTorrentFile { Path = "folder/file1.mkv", Size = 1048576 }
            }
        };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);

        try
        {
            using var doc = JsonDocument.Parse($"{{\"method\":\"web.get_torrent_info\",\"params\":[\"{tempPath.Replace("\\", "\\\\")}\"],\"id\":1}}");
            var result = await this.controller.HandleRpc(doc.RootElement);

            result.Should().BeOfType<JsonResult>();
            var jsonResult = (JsonResult)result;
            var json = JsonSerializer.Serialize(jsonResult.Value);
            json.Should().Contain("\"name\":\"Test Torrent\"");
            json.Should().Contain("\"info_hash\":\"abcdef1234567890abcdef1234567890abcdef12\"");
            json.Should().Contain("\"total_size\":1048576");
            json.Should().Contain("\"files_tree\"");
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }
    }

    [Test]
    public async Task HandleRpc_WebAddTorrents_AddsTorrentsAndCleansUpTempFiles()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"deluge_add_test_{System.Guid.NewGuid():N}.torrent");
        var dummyBytes = new byte[] { 5, 6, 7, 8 };
        await System.IO.File.WriteAllBytesAsync(tempPath, dummyBytes);

        var parsed = new ParsedTorrent
        {
            InfoHash = "1234567890123456789012345678901234567890",
            Name = "Add Test Torrent",
            TotalSize = 2048,
        };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>())
            .Returns(Task.FromResult(new Torrent { Id = 42, InfoHash = parsed.InfoHash, TargetRatio = 2.0 }));

        var escapedPath = tempPath.Replace("\\", "\\\\");
        using var doc = JsonDocument.Parse($"{{\"method\":\"web.add_torrents\",\"params\":[[{{\"path\":\"{escapedPath}\",\"options\":{{\"download_location\":\"/downloads\",\"stop_ratio\":2.0,\"add_paused\":true}}}}]],\"id\":1}}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");

        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsed,
            null,
            "/downloads",
            true,
            Arg.Any<byte[]>());
        await this.torrentService.Received(1).UpdateAsync(Arg.Is<Torrent>(t => t.TargetRatio == 2.0));

        System.IO.File.Exists(tempPath).Should().BeFalse();
    }
}
