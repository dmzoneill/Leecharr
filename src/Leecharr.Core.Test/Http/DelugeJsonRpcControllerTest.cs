// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Deluge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
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
        context.Response.Headers["Set-Cookie"].ToString().Should().Contain("_session_id");
        context.Response.Headers["Set-Cookie"].ToString().Should().Contain("deluge-session");
    }

    [Test]
    public async Task HandleRpc_ManagementMethod_WhenUnauthenticated_Returns200WithJsonRpcErrorEnvelope()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":null");
        json.Should().Contain("\"message\":\"Not authenticated\"");
        json.Should().Contain("\"code\":1");
        json.Should().Contain("\"id\":1");
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
    public async Task HandleRpc_CoreSetTorrentOptions_WithMoveCompletedPath_DoesNotPrematurelyRelocateFiles()
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
        await this.torrentService.DidNotReceive().SetLocationAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>());
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
    public async Task HandleRpc_CoreGetFreeSpaceBytes_WithCustomPath_QueriesDiskProvider()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(350_000_000_000L);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_free_space_bytes\",\"params\":[\"/mnt/storage\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":350000000000");
    }

    [Test]
    public async Task HandleRpc_CoreEnableAndDisablePlugin_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var enableDoc = JsonDocument.Parse("{\"method\":\"core.enable_plugin\",\"params\":[\"Label\"],\"id\":1}");
        var enableResult = await this.controller.HandleRpc(enableDoc.RootElement);

        enableResult.Should().BeOfType<JsonResult>();
        var enableJson = JsonSerializer.Serialize(((JsonResult)enableResult).Value);
        enableJson.Should().Contain("\"result\":true");

        using var disableDoc = JsonDocument.Parse("{\"method\":\"core.disable_plugin\",\"params\":[\"Label\"],\"id\":2}");
        var disableResult = await this.controller.HandleRpc(disableDoc.RootElement);

        disableResult.Should().BeOfType<JsonResult>();
        var disableJson = JsonSerializer.Serialize(((JsonResult)disableResult).Value);
        disableJson.Should().Contain("\"result\":true");
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

    [Test]
    public async Task HandleRpc_CoreGetTorrentsStatus_WithNestedSavePath_ResolvesSavePathCorrectly()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 1,
            Name = "Spectre 2015",
            InfoHash = "1234567890123456789012345678901234567890",
            SavePath = "/downloads/Spectre 2015",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{},[\"name\",\"save_path\",\"download_location\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"save_path\":\"/downloads\"");
        json.Should().Contain("\"download_location\":\"/downloads\"");
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
    public async Task HandleRpc_CoreAddTorrentMagnet_WithVariousBooleanFormatsForAddPaused_DoesNotThrowAndParsesCorrectly(string addPausedJson, bool expectedPaused)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var magnetUri = "magnet:?xt=urn:btih:1234567890123456789012345678901234567890&dn=Test";
        var added = new Torrent
        {
            Id = 1,
            InfoHash = "1234567890123456789012345678901234567890",
            Name = "Test",
        };
        this.torrentService.AddFromMagnetAsync(magnetUri, null, null, expectedPaused).Returns(added);

        var json = $"{{\"method\":\"core.add_torrent_magnet\",\"params\":[\"{magnetUri}\",{{\"add_paused\":{addPausedJson}}}],\"id\":1}}";
        using var doc = JsonDocument.Parse(json);
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var resultJson = JsonSerializer.Serialize(jsonResult.Value);
        resultJson.Should().Contain(added.InfoHash);

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
    public async Task HandleRpc_CoreRemoveTorrent_WithVariousBooleanFormatsForDeleteData_DoesNotThrowAndParsesCorrectly(string deleteDataJson, bool expectedDelete)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var infoHash = "1234567890123456789012345678901234567890";
        var torrent = new Torrent { Id = 42, InfoHash = infoHash };
        this.torrentService.GetByInfoHash(infoHash).Returns(torrent);

        var json = $"{{\"method\":\"core.remove_torrent\",\"params\":[\"{infoHash}\",{deleteDataJson}],\"id\":1}}";
        using var doc = JsonDocument.Parse(json);
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var resultJson = JsonSerializer.Serialize(jsonResult.Value);
        resultJson.Should().Contain("\"result\":true");

        await this.torrentService.Received(1).DeleteAsync(42, expectedDelete);
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentsStatus_IncludesAllNewStatusKeysAndNumPeersAsLeechers()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Deluge Status Test Torrent",
            InfoHash = "1122334455667788990011223344556677889900",
            TotalSize = 1048576,
            Downloaded = 524288,
            Uploaded = 262144,
            Progress = 0.5,
            DownloadSpeed = 1024,
            UploadSpeed = 512,
            Eta = 512,
            Ratio = 0.5,
            Seeders = 10,
            Leechers = 5,
            PieceCount = 256,
            PieceLength = 4096,
            QueuePosition = 2,
            SequentialDownload = true,
            Status = TorrentStatus.Downloading,
            TrackerUrl = "http://tracker.example.com:80/announce",
            Category = "tv",
            DateAdded = new System.DateTime(2025, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
            DateCompleted = new System.DateTime(2025, 1, 2, 0, 0, 0, System.DateTimeKind.Utc),
            LastActive = System.DateTime.UtcNow.AddSeconds(-30),
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{}, []],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);

        using var resultDoc = JsonDocument.Parse(json);
        var resObj = resultDoc.RootElement.GetProperty("result");
        var status = resObj.GetProperty("1122334455667788990011223344556677889900");

        // num_peers must strictly be leechers (5), total_peers must be (15)
        status.GetProperty("num_peers").GetInt32().Should().Be(5);
        status.GetProperty("total_peers").GetInt32().Should().Be(15);
        status.GetProperty("num_seeds").GetInt32().Should().Be(10);
        status.GetProperty("total_seeds").GetInt32().Should().Be(10);

        // missing status keys
        status.GetProperty("total_uploaded").GetInt64().Should().Be(262144);
        status.GetProperty("seeds_peers_ratio").GetDouble().Should().BeApproximately(2.0, 0.001);
        status.GetProperty("tracker").GetString().Should().Be("http://tracker.example.com:80/announce");
        status.GetProperty("tracker_host").GetString().Should().Be("tracker.example.com");
        status.GetProperty("trackers").ValueKind.Should().Be(JsonValueKind.Array);
        status.GetProperty("tracker_status").GetString().Should().Contain("OK");
        status.GetProperty("next_announce").GetInt32().Should().Be(1800);
        status.GetProperty("finished_time").GetInt64().Should().BeGreaterThan(0);
        status.GetProperty("time_since_transfer").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        status.GetProperty("num_pieces").GetInt32().Should().Be(256);
        status.GetProperty("piece_length").GetInt32().Should().Be(4096);
        status.GetProperty("distributed_copies").GetDouble().Should().Be(0.5);
        status.GetProperty("queue_position").GetInt32().Should().Be(2);
        status.GetProperty("storage_mode").GetString().Should().Be("sparse");
        status.GetProperty("move_completed").GetBoolean().Should().BeFalse();
        status.GetProperty("move_completed_path").GetString().Should().NotBeNull();
        status.GetProperty("prioritize_first_last_pieces").GetBoolean().Should().BeFalse();
        status.GetProperty("sequential_download").GetBoolean().Should().BeTrue();
        status.GetProperty("max_connections").GetInt32().Should().Be(-1);
        status.GetProperty("max_upload_slots").GetInt32().Should().Be(-1);
    }

    [Test]
    public async Task HandleRpc_CoreGetConfig_ReturnsExpandedDelugePreferences()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/custom/downloads");
        this.configService.MaxGlobalConnections.Returns(250);
        this.configService.MaxPerTorrentConnections.Returns(60);
        this.configService.MaxUploadSlots.Returns(8);
        this.configService.MaxDownloadSpeedKbps.Returns(5000);
        this.configService.MaxUploadSpeedKbps.Returns(2000);
        this.configService.MaxActiveTorrents.Returns(10);
        this.configService.MaxActiveDownloads.Returns(6);
        this.configService.MaxActiveUploads.Returns(4);
        this.configService.EnableDht.Returns(true);
        this.configService.UpnpEnabled.Returns(true);
        this.configService.EnableLpd.Returns(true);
        this.configService.BindInterface.Returns("eth0");
        this.configService.PeerPortRandomOnStart.Returns(false);
        this.configService.ListeningPort.Returns(58846);
        this.configService.EncryptionMode.Returns("Forced");
        this.configService.GlobalSeedRatioLimit.Returns(1.5);
        this.configService.IdleSeedingLimitMinutes.Returns(120);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_config\",\"params\":[],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);

        using var resultDoc = JsonDocument.Parse(json);
        var config = resultDoc.RootElement.GetProperty("result");

        config.GetProperty("download_location").GetString().Should().Be("/custom/downloads");
        config.GetProperty("dht").GetBoolean().Should().BeTrue();
        config.GetProperty("upnp").GetBoolean().Should().BeTrue();
        config.GetProperty("natpmp").GetBoolean().Should().BeTrue();
        config.GetProperty("lsd").GetBoolean().Should().BeTrue();
        config.GetProperty("listen_interface").GetString().Should().Be("eth0");
        config.GetProperty("random_port").GetBoolean().Should().BeFalse();
        config.GetProperty("enc_in_policy").GetInt32().Should().Be(0);
        config.GetProperty("enc_out_policy").GetInt32().Should().Be(0);
        config.GetProperty("enc_prefer_rc4").GetBoolean().Should().BeTrue();
        config.GetProperty("stop_seed_at_ratio").GetBoolean().Should().BeTrue();
        config.GetProperty("stop_seed_ratio").GetDouble().Should().Be(1.5);
        config.GetProperty("seed_time_limit").GetInt32().Should().Be(7200);
        config.GetProperty("max_connections_per_torrent").GetInt32().Should().Be(60);
        config.GetProperty("max_upload_slots_global").GetInt32().Should().Be(8);
    }

    [Test]
    public async Task HandleRpc_CoreSetConfig_UpdatesExtendedPreferences()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var setConfigJson = "{\"method\":\"core.set_config\",\"params\":[{" +
            "\"dht\":true," +
            "\"upnp\":false," +
            "\"lsd\":true," +
            "\"listen_interface\":\"tun0\"," +
            "\"random_port\":true," +
            "\"stop_seed_ratio\":3.0," +
            "\"seed_time_limit\":3600," +
            "\"enc_in_policy\":2," +
            "\"max_connections_per_torrent\":40," +
            "\"max_upload_slots_global\":12" +
            "}],\"id\":1}";

        using var doc = JsonDocument.Parse(setConfigJson);
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (bool)d["EnableDht"] == true &&
            (bool)d["UpnpEnabled"] == false &&
            (bool)d["EnableLpd"] == true &&
            (string)d["BindInterface"] == "tun0" &&
            (bool)d["PeerPortRandomOnStart"] == true &&
            (double)d["GlobalSeedRatioLimit"] == 3.0 &&
            (int)d["IdleSeedingLimitMinutes"] == 60 &&
            (string)d["EncryptionMode"] == "Disabled" &&
            (int)d["MaxPerTorrentConnections"] == 40 &&
            (int)d["MaxUploadSlots"] == 12));
    }

    [Test]
    public async Task HandleRpc_CoreForceReannounce_InvokesForceAnnounceAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var infoHash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrent = new Torrent { Id = 77, InfoHash = infoHash };
        this.torrentService.GetByInfoHash(infoHash).Returns(torrent);

        using var doc = JsonDocument.Parse($"{{\"method\":\"core.force_reannounce\",\"params\":[[\"{infoHash}\"]],\"id\":1}}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");

        await this.torrentService.Received(1).ForceAnnounceAsync(77);
    }

    [Test]
    public async Task HandleRpc_CoreAddTorrentFileAsync_AliasesToAddTorrentFile()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var parsed = new ParsedTorrent
        {
            InfoHash = "9988776655443322110099887766554433221100",
            Name = "Async Added Torrent",
            TotalSize = 1024,
        };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>())
            .Returns(Task.FromResult(new Torrent { Id = 88, InfoHash = parsed.InfoHash }));

        var dummyB64 = System.Convert.ToBase64String(new byte[] { 1, 2, 3 });
        using var doc = JsonDocument.Parse($"{{\"method\":\"core.add_torrent_file_async\",\"params\":[\"test.torrent\", \"{dummyB64}\", {{\"download_location\":\"/downloads\"}}],\"id\":1}}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain(parsed.InfoHash);

        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, null, "/downloads", false, Arg.Any<byte[]>());
    }

    [Test]
    public async Task HandleRpc_CoreRenameFiles_WithArrayPairs_InvokesRenameFileAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var infoHash = "1111222233334444555566667777888899990000";
        var torrent = new Torrent { Id = 99, InfoHash = infoHash };
        this.torrentService.GetByInfoHash(infoHash).Returns(torrent);

        var files = new List<TorrentFile>
        {
            new TorrentFile { Id = 1, TorrentId = 99, Path = "folder/old_video.mkv", Size = 1000 },
            new TorrentFile { Id = 2, TorrentId = 99, Path = "folder/old_sub.srt", Size = 100 },
        };
        this.torrentFileService.GetFiles(99).Returns(files);

        using var doc = JsonDocument.Parse($"{{\"method\":\"core.rename_files\",\"params\":[\"{infoHash}\", [[0, \"folder/new_video.mkv\"], [1, \"folder/new_sub.srt\"]]],\"id\":1}}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");

        await this.torrentService.Received(1).RenameFileAsync(99, "folder/old_video.mkv", "folder/new_video.mkv");
        await this.torrentService.Received(1).RenameFileAsync(99, "folder/old_sub.srt", "folder/new_sub.srt");
    }

    [Test]
    public async Task HandleRpc_LabelGetTorrents_FiltersTorrentsByLabel()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent1 = new Torrent { Id = 1, InfoHash = "aaaa000000000000000000000000000000000001", Category = "movies" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "bbbb000000000000000000000000000000000002", Category = "tv" };
        var torrent3 = new Torrent { Id = 3, InfoHash = "cccc000000000000000000000000000000000003", Category = null, Label = string.Empty };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2, torrent3 });

        // Query by "movies"
        using var docMovies = JsonDocument.Parse("{\"method\":\"label.get_torrents\",\"params\":[\"movies\"],\"id\":1}");
        var resultMovies = await this.controller.HandleRpc(docMovies.RootElement);
        var jsonMovies = JsonSerializer.Serialize(((JsonResult)resultMovies).Value);
        jsonMovies.Should().Contain(torrent1.InfoHash.ToLowerInvariant());
        jsonMovies.Should().NotContain(torrent2.InfoHash.ToLowerInvariant());

        // Query by "All"
        using var docAll = JsonDocument.Parse("{\"method\":\"label.get_torrents\",\"params\":[\"All\"],\"id\":2}");
        var resultAll = await this.controller.HandleRpc(docAll.RootElement);
        var jsonAll = JsonSerializer.Serialize(((JsonResult)resultAll).Value);
        jsonAll.Should().Contain(torrent1.InfoHash.ToLowerInvariant());
        jsonAll.Should().Contain(torrent2.InfoHash.ToLowerInvariant());
        jsonAll.Should().Contain(torrent3.InfoHash.ToLowerInvariant());

        // Query by "no_label"
        using var docNoLabel = JsonDocument.Parse("{\"method\":\"label.get_torrents\",\"params\":[\"no_label\"],\"id\":3}");
        var resultNoLabel = await this.controller.HandleRpc(docNoLabel.RootElement);
        var jsonNoLabel = JsonSerializer.Serialize(((JsonResult)resultNoLabel).Value);
        jsonNoLabel.Should().NotContain(torrent1.InfoHash.ToLowerInvariant());
        jsonNoLabel.Should().NotContain(torrent2.InfoHash.ToLowerInvariant());
        jsonNoLabel.Should().Contain(torrent3.InfoHash.ToLowerInvariant());
    }

    [Test]
    public async Task HandleRpc_AuthWithSessionIdCookie_Succeeds()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        // 1. Log in to get session cookie
        using var loginDoc = JsonDocument.Parse("{\"method\":\"auth.login\",\"params\":[\"deluge_secret_key\"],\"id\":1}");
        var loginResult = await this.controller.HandleRpc(loginDoc.RootElement);
        loginResult.Should().BeOfType<JsonResult>();

        var cookiesHeader = context.Response.Headers["Set-Cookie"].ToString();
        var match = System.Text.RegularExpressions.Regex.Match(cookiesHeader, @"_session_id=([a-f0-9]+)");
        match.Success.Should().BeTrue();
        var sid = match.Groups[1].Value;

        // 2. Perform RPC with _session_id cookie in request
        var authContext = new DefaultHttpContext();
        authContext.Request.Headers["Cookie"] = $"_session_id={sid}";
        this.controller.ControllerContext = new ControllerContext { HttpContext = authContext };

        using var statusDoc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[],\"id\":2}");
        var statusResult = await this.controller.HandleRpc(statusDoc.RootElement);

        statusResult.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)statusResult;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"error\":null");
        json.Should().Contain("\"result\":");
    }

    [Test]
    public async Task HandleRpc_AuthDeleteSession_InvalidatesSessionAndDeletesCookies()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        // 1. Log in
        using var loginDoc = JsonDocument.Parse("{\"method\":\"auth.login\",\"params\":[\"deluge_secret_key\"],\"id\":1}");
        await this.controller.HandleRpc(loginDoc.RootElement);

        var cookiesHeader = context.Response.Headers["Set-Cookie"].ToString();
        var match = System.Text.RegularExpressions.Regex.Match(cookiesHeader, @"_session_id=([a-f0-9]+)");
        var sid = match.Groups[1].Value;

        // 2. Delete session
        var logoutContext = new DefaultHttpContext();
        logoutContext.Request.Headers["Cookie"] = $"_session_id={sid}; deluge-session={sid}";
        this.controller.ControllerContext = new ControllerContext { HttpContext = logoutContext };

        using var logoutDoc = JsonDocument.Parse("{\"method\":\"auth.delete_session\",\"params\":[],\"id\":2}");
        var logoutResult = await this.controller.HandleRpc(logoutDoc.RootElement);

        logoutResult.Should().BeOfType<JsonResult>();
        logoutContext.Response.Headers["Set-Cookie"].ToString().Should().Contain("_session_id=");
        logoutContext.Response.Headers["Set-Cookie"].ToString().Should().Contain("deluge-session=");

        // 3. Try request with old session ID -> should fail auth
        var reqContext = new DefaultHttpContext();
        reqContext.Request.Headers["Cookie"] = $"_session_id={sid}";
        this.controller.ControllerContext = new ControllerContext { HttpContext = reqContext };

        using var testDoc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[],\"id\":3}");
        var testResult = await this.controller.HandleRpc(testDoc.RootElement);

        testResult.Should().BeOfType<JsonResult>();
        var json = JsonSerializer.Serialize(((JsonResult)testResult).Value);
        json.Should().Contain("\"message\":\"Not authenticated\"");
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentsStatus_WithStateFilters_ReturnsMatchingTorrents()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tActive = new Torrent { Id = 1, InfoHash = "1111111111111111111111111111111111111111", Status = TorrentStatus.Downloading, DownloadSpeed = 1000, UploadSpeed = 0 };
        var tInactive = new Torrent { Id = 2, InfoHash = "2222222222222222222222222222222222222222", Status = TorrentStatus.Paused, DownloadSpeed = 0, UploadSpeed = 0 };
        var tSeeding = new Torrent { Id = 3, InfoHash = "3333333333333333333333333333333333333333", Status = TorrentStatus.Seeding, DownloadSpeed = 0, UploadSpeed = 500 };

        this.torrentService.GetAll().Returns(new List<Torrent> { tActive, tInactive, tSeeding });

        // Filter: Active
        using var docActive = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"state\":\"Active\"},[\"name\"]],\"id\":1}");
        var resActive = await this.controller.HandleRpc(docActive.RootElement);
        var jsonActive = JsonSerializer.Serialize(((JsonResult)resActive).Value);
        jsonActive.Should().Contain(tActive.InfoHash);
        jsonActive.Should().NotContain(tInactive.InfoHash);
        jsonActive.Should().Contain(tSeeding.InfoHash); // UploadSpeed > 0 is Active

        // Filter: Inactive
        using var docInactive = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"state\":\"Inactive\"},[\"name\"]],\"id\":2}");
        var resInactive = await this.controller.HandleRpc(docInactive.RootElement);
        var jsonInactive = JsonSerializer.Serialize(((JsonResult)resInactive).Value);
        jsonInactive.Should().NotContain(tActive.InfoHash);
        jsonInactive.Should().Contain(tInactive.InfoHash);
        jsonInactive.Should().NotContain(tSeeding.InfoHash);

        // Filter: Seeding
        using var docSeeding = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"state\":\"Seeding\"},[\"name\"]],\"id\":3}");
        var resSeeding = await this.controller.HandleRpc(docSeeding.RootElement);
        var jsonSeeding = JsonSerializer.Serialize(((JsonResult)resSeeding).Value);
        jsonSeeding.Should().NotContain(tActive.InfoHash);
        jsonSeeding.Should().NotContain(tInactive.InfoHash);
        jsonSeeding.Should().Contain(tSeeding.InfoHash);

        // Filter: Array state ["Downloading", "Seeding"]
        using var docArray = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"state\":[\"Downloading\",\"Seeding\"]},[\"name\"]],\"id\":4}");
        var resArray = await this.controller.HandleRpc(docArray.RootElement);
        var jsonArray = JsonSerializer.Serialize(((JsonResult)resArray).Value);
        jsonArray.Should().Contain(tActive.InfoHash);
        jsonArray.Should().NotContain(tInactive.InfoHash);
        jsonArray.Should().Contain(tSeeding.InfoHash);
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentsStatus_WithLabelAndIdFilters_ReturnsMatchingTorrents()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var t1 = new Torrent { Id = 1, InfoHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Category = "tv" };
        var t2 = new Torrent { Id = 2, InfoHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", Category = "movies" };
        var t3 = new Torrent { Id = 3, InfoHash = "cccccccccccccccccccccccccccccccccccccccc", Category = null, Label = string.Empty };

        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3 });

        // Filter: label = All
        using var docAll = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"label\":\"All\"},[\"name\"]],\"id\":1}");
        var resAll = await this.controller.HandleRpc(docAll.RootElement);
        var jsonAll = JsonSerializer.Serialize(((JsonResult)resAll).Value);
        jsonAll.Should().Contain(t1.InfoHash);
        jsonAll.Should().Contain(t2.InfoHash);
        jsonAll.Should().Contain(t3.InfoHash);

        // Filter: label = None
        using var docNone = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"label\":\"None\"},[\"name\"]],\"id\":2}");
        var resNone = await this.controller.HandleRpc(docNone.RootElement);
        var jsonNone = JsonSerializer.Serialize(((JsonResult)resNone).Value);
        jsonNone.Should().NotContain(t1.InfoHash);
        jsonNone.Should().NotContain(t2.InfoHash);
        jsonNone.Should().Contain(t3.InfoHash);

        // Filter: label = ""
        using var docEmptyLabel = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"label\":\"\"},[\"name\"]],\"id\":3}");
        var resEmptyLabel = await this.controller.HandleRpc(docEmptyLabel.RootElement);
        var jsonEmptyLabel = JsonSerializer.Serialize(((JsonResult)resEmptyLabel).Value);
        jsonEmptyLabel.Should().NotContain(t1.InfoHash);
        jsonEmptyLabel.Should().NotContain(t2.InfoHash);
        jsonEmptyLabel.Should().Contain(t3.InfoHash);

        // Filter: id array query
        using var docIdList = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"id\":[\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"cccccccccccccccccccccccccccccccccccccccc\"]},[\"name\"]],\"id\":4}");
        var resIdList = await this.controller.HandleRpc(docIdList.RootElement);
        var jsonIdList = JsonSerializer.Serialize(((JsonResult)resIdList).Value);
        jsonIdList.Should().Contain(t1.InfoHash);
        jsonIdList.Should().NotContain(t2.InfoHash);
        jsonIdList.Should().Contain(t3.InfoHash);

        // Filter: hash scalar query
        using var docHash = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"hash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"},[\"name\"]],\"id\":5}");
        var resHash = await this.controller.HandleRpc(docHash.RootElement);
        var jsonHash = JsonSerializer.Serialize(((JsonResult)resHash).Value);
        jsonHash.Should().NotContain(t1.InfoHash);
        jsonHash.Should().Contain(t2.InfoHash);
        jsonHash.Should().NotContain(t3.InfoHash);

        // Filter: info_hash query
        using var docInfoHash = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"info_hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"},[\"name\"]],\"id\":6}");
        var resInfoHash = await this.controller.HandleRpc(docInfoHash.RootElement);
        var jsonInfoHash = JsonSerializer.Serialize(((JsonResult)resInfoHash).Value);
        jsonInfoHash.Should().Contain(t1.InfoHash);
        jsonInfoHash.Should().NotContain(t2.InfoHash);
        jsonInfoHash.Should().NotContain(t3.InfoHash);

        // Filter: label array query
        using var docLabelArray = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"label\":[\"tv\",\"movies\"]},[\"name\"]],\"id\":7}");
        var resLabelArray = await this.controller.HandleRpc(docLabelArray.RootElement);
        var jsonLabelArray = JsonSerializer.Serialize(((JsonResult)resLabelArray).Value);
        jsonLabelArray.Should().Contain(t1.InfoHash);
        jsonLabelArray.Should().Contain(t2.InfoHash);
        jsonLabelArray.Should().NotContain(t3.InfoHash);

        // Filter: integer ID matching t.Id.ToString()
        using var docIntId = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"id\":[1,3]},[\"name\"]],\"id\":8}");
        var resIntId = await this.controller.HandleRpc(docIntId.RootElement);
        var jsonIntId = JsonSerializer.Serialize(((JsonResult)resIntId).Value);
        jsonIntId.Should().Contain(t1.InfoHash);
        jsonIntId.Should().NotContain(t2.InfoHash);
        jsonIntId.Should().Contain(t3.InfoHash);
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentsStatus_WithTrackerHostFilters_ReturnsMatchingTorrents()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var t1 = new Torrent { Id = 1, InfoHash = "1111111111111111111111111111111111111111", TrackerUrl = "http://tracker1.example.com:8080/announce" };
        var t2 = new Torrent { Id = 2, InfoHash = "2222222222222222222222222222222222222222", TrackerUrl = "http://tracker2.example.com:8080/announce" };
        var t3 = new Torrent { Id = 3, InfoHash = "3333333333333333333333333333333333333333", TrackerUrl = null };

        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3 });

        // Filter: tracker_host scalar
        using var docScalar = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"tracker_host\":\"tracker1.example.com\"},[\"name\"]],\"id\":1}");
        var resScalar = await this.controller.HandleRpc(docScalar.RootElement);
        var jsonScalar = JsonSerializer.Serialize(((JsonResult)resScalar).Value);
        jsonScalar.Should().Contain(t1.InfoHash);
        jsonScalar.Should().NotContain(t2.InfoHash);
        jsonScalar.Should().NotContain(t3.InfoHash);

        // Filter: tracker_host array
        using var docArray = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{\"tracker_host\":[\"tracker1.example.com\",\"tracker2.example.com\"]},[\"name\"]],\"id\":2}");
        var resArray = await this.controller.HandleRpc(docArray.RootElement);
        var jsonArray = JsonSerializer.Serialize(((JsonResult)resArray).Value);
        jsonArray.Should().Contain(t1.InfoHash);
        jsonArray.Should().Contain(t2.InfoHash);
        jsonArray.Should().NotContain(t3.InfoHash);
    }

    [Test]
    public async Task HandleRpc_BuildFilterTree_IncludesOwnerAndAllNoneLabels()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "1111111111111111111111111111111111111111", Category = "tv", DownloadSpeed = 1000 },
            new Torrent { Id = 2, InfoHash = "2222222222222222222222222222222222222222", Category = null },
        };
        this.torrentService.GetAll().Returns(torrents);
        this.categoryService.GetAll().Returns(new List<Category> { new Category { Id = 1, Name = "tv" } });

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_filter_tree\",\"params\":[],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var json = JsonSerializer.Serialize(((JsonResult)result).Value);

        json.Should().Contain("\"owner\"");
        json.Should().Contain("\"All\",2");
        json.Should().Contain("\"None\",1");
        json.Should().Contain("\"tv\",1");
        json.Should().Contain("\"Active\",1");
    }

    [Test]
    public async Task HandleRpc_GetFreeSpace_ReturnsAvailableSpaceFromDiskProvider()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.diskProvider.GetAvailableSpace("/downloads").Returns(500_000_000_000L);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_free_space\",\"params\":[\"/downloads\"],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var json = JsonSerializer.Serialize(((JsonResult)result).Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetInt64().Should().Be(500_000_000_000L);
    }

    [Test]
    public async Task HandleRpc_GetTorrentsStatus_ReturnsStandardDelugeMetrics()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        const string hash = "1122334455667788990011223344556677889900";
        var torrent = new Torrent
        {
            Id = 10,
            Name = "DelugeStandardMetricsTorrent",
            InfoHash = hash,
            Status = TorrentStatus.Downloading,
            Progress = 0.45,
            TotalSize = 104857600L,
            PieceCount = 400,
            PieceLength = 262144,
            QueuePosition = 2,
            SavePath = "/downloads/tv",
            TrackerUrl = "http://tracker.deluge-test.org:8080/announce",
            Comment = "Deluge release comment",
            CreatedBy = "DelugeTester/1.0",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var requestedKeys = new[]
        {
            "download_location",
            "queue",
            "queue_position",
            "num_pieces",
            "piece_length",
            "tracker_host",
            "trackers",
            "total_wanted",
            "comment",
            "creator",
            "owner",
            "auto_managed",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            method = "core.get_torrents_status",
            @params = new object[] { new { }, requestedKeys },
            id = 456,
        }));

        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var json = JsonSerializer.Serialize(((JsonResult)result).Value);
        using var resDoc = JsonDocument.Parse(json);
        var resultElem = resDoc.RootElement.GetProperty("result");
        var torrentObj = resultElem.GetProperty(hash.ToLowerInvariant());

        torrentObj.GetProperty("download_location").GetString().Should().Be("/downloads/tv");
        torrentObj.GetProperty("queue").GetInt32().Should().Be(2);
        torrentObj.GetProperty("queue_position").GetInt32().Should().Be(2);
        torrentObj.GetProperty("num_pieces").GetInt32().Should().Be(400);
        torrentObj.GetProperty("piece_length").GetInt32().Should().Be(262144);
        torrentObj.GetProperty("tracker_host").GetString().Should().Be("tracker.deluge-test.org");
        torrentObj.GetProperty("trackers").GetArrayLength().Should().Be(1);
        torrentObj.GetProperty("trackers")[0].GetProperty("url").GetString().Should().Be("http://tracker.deluge-test.org:8080/announce");
        torrentObj.GetProperty("trackers")[0].GetProperty("tier").GetInt32().Should().Be(0);
        torrentObj.GetProperty("total_wanted").GetInt64().Should().Be(104857600L);
        torrentObj.GetProperty("comment").GetString().Should().Be("Deluge release comment");
        torrentObj.GetProperty("creator").GetString().Should().Be("DelugeTester/1.0");
        torrentObj.GetProperty("owner").GetString().Should().Be("admin");
        torrentObj.GetProperty("auto_managed").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task HandleRpc_GetTorrentStatus_ReturnsStandardDelugeMetrics()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        const string hash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrent = new Torrent
        {
            Id = 11,
            Name = "SingleTorrentTest",
            InfoHash = hash,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            TotalSize = 52428800L,
            PieceCount = 200,
            PieceLength = 262144,
            QueuePosition = 1,
            SavePath = "/downloads/movies",
            TrackerUrl = "http://tracker.example.com/announce",
            Comment = "Movie torrent comment",
            CreatedBy = "Leecharr/1.0",
        };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var requestedKeys = new[]
        {
            "download_location",
            "queue",
            "num_pieces",
            "piece_length",
            "tracker_host",
            "trackers",
            "total_wanted",
            "comment",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            method = "core.get_torrent_status",
            @params = new object[] { hash, requestedKeys },
            id = 457,
        }));

        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var json = JsonSerializer.Serialize(((JsonResult)result).Value);
        using var resDoc = JsonDocument.Parse(json);
        var torrentObj = resDoc.RootElement.GetProperty("result");

        torrentObj.GetProperty("download_location").GetString().Should().Be("/downloads/movies");
        torrentObj.GetProperty("queue").GetInt32().Should().Be(1);
        torrentObj.GetProperty("num_pieces").GetInt32().Should().Be(200);
        torrentObj.GetProperty("piece_length").GetInt32().Should().Be(262144);
        torrentObj.GetProperty("tracker_host").GetString().Should().Be("tracker.example.com");
        torrentObj.GetProperty("trackers").GetArrayLength().Should().Be(1);
        torrentObj.GetProperty("total_wanted").GetInt64().Should().Be(52428800L);
        torrentObj.GetProperty("comment").GetString().Should().Be("Movie torrent comment");
    }

    [Test]
    public async Task HandleRpc_CoreSetTorrentOptions_WithNullAndNonStringValues_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        const string hash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = hash,
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        using var doc = JsonDocument.Parse("{\"method\":\"core.set_torrent_options\",\"params\":[[\"aabbccddeeff00112233445566778899aabbccdd\"], {\"download_location\": null, \"move_completed_path\": null, \"max_download_speed\": null, \"max_upload_speed\": null, \"stop_ratio\": null, \"file_priorities\": null}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
        await this.torrentService.DidNotReceive().SetLocationAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleRpc_CoreAddTorrentMagnet_WithNullAndNonStringOptions_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        const string magnet = "magnet:?xt=urn:btih:aabbccddeeff00112233445566778899aabbccdd&dn=Test";
        this.torrentService.AddFromMagnetAsync(magnet, null, null, false)
            .Returns(Task.FromResult<Torrent>(new Torrent { Id = 1, InfoHash = "aabbccddeeff00112233445566778899aabbccdd" }));

        using var doc = JsonDocument.Parse("{\"method\":\"core.add_torrent_magnet\",\"params\":[\"magnet:?xt=urn:btih:aabbccddeeff00112233445566778899aabbccdd&dn=Test\", {\"download_location\": null, \"move_completed_path\": null, \"label\": null, \"stop_ratio\": null, \"add_paused\": null}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":\"aabbccddeeff00112233445566778899aabbccdd\"");
    }

    [Test]
    public async Task HandleRpc_CoreAddTorrentFile_WithNullAndNonStringOptions_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var parsedTorrent = new ParsedTorrent { InfoHash = "aabbccddeeff00112233445566778899aabbccdd", Name = "Test" };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedTorrent);
        this.torrentService.AddFromParsedTorrentAsync(parsedTorrent, null, null, false, Arg.Any<byte[]>())
            .Returns(Task.FromResult<Torrent>(new Torrent { Id = 1, InfoHash = "aabbccddeeff00112233445566778899aabbccdd" }));

        var dummyB64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        using var doc = JsonDocument.Parse($"{{\"method\":\"core.add_torrent_file\",\"params\":[\"test.torrent\", \"{dummyB64}\", {{\"download_location\": null, \"move_completed_path\": null, \"label\": null, \"stop_ratio\": null, \"add_paused\": null}}],\"id\":1}}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":\"aabbccddeeff00112233445566778899aabbccdd\"");
    }

    [Test]
    public async Task HandleRpc_LabelSetOptions_WithNullOptions_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.categoryService.GetByName("tv").Returns(new Category { Id = 1, Name = "tv" });

        using var doc = JsonDocument.Parse("{\"method\":\"label.set_options\",\"params\":[\"tv\", {\"move_completed_path\": null, \"stop_ratio\": null}],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
    }

    [Test]
    public async Task HandleRpc_LabelSetTorrent_WithNullValues_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"label.set_torrent\",\"params\":[null, null],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"result\":true");
    }

    [Test]
    public async Task HandleRpc_CoreGetConfigValues_WithNullElements_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_config_values\",\"params\":[[null, \"download_location\", 123]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("\"error\":null");
    }

    [Test]
    public async Task HandleRpc_Multicall_AuthLoginFollowedByCoreGetConfig_SuccessfullyAuthenticatesSubsequentCall()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("[{\"method\":\"auth.login\",\"params\":[\"deluge_secret_key\"],\"id\":1},{\"method\":\"core.get_config\",\"params\":[],\"id\":2}]");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);

        json.Should().NotContain("Not authenticated");
        json.Should().Contain("\"id\":1");
        json.Should().Contain("\"id\":2");

        using var responseDoc = JsonDocument.Parse(json);
        responseDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        responseDoc.RootElement.GetArrayLength().Should().Be(2);

        var first = responseDoc.RootElement[0];
        first.GetProperty("result").GetBoolean().Should().BeTrue();
        first.GetProperty("id").GetInt64().Should().Be(1);

        var second = responseDoc.RootElement[1];
        second.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        second.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Object);
        second.GetProperty("id").GetInt64().Should().Be(2);
    }

    [Test]
    public async Task HandleRpc_Multicall_AuthLoginFollowedByWebGetTorrentsStatus_SuccessfullyAuthenticatesSubsequentCall()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.torrentService.GetAll().Returns(new List<Torrent>());

        using var doc = JsonDocument.Parse("[{\"method\":\"auth.login\",\"params\":[\"deluge_secret_key\"],\"id\":1},{\"method\":\"web.get_torrents_status\",\"params\":[{}, []],\"id\":2}]");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);

        json.Should().NotContain("Not authenticated");

        using var responseDoc = JsonDocument.Parse(json);
        responseDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        responseDoc.RootElement.GetArrayLength().Should().Be(2);

        var first = responseDoc.RootElement[0];
        first.GetProperty("result").GetBoolean().Should().BeTrue();

        var second = responseDoc.RootElement[1];
        second.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        second.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Object);
        second.GetProperty("result").GetProperty("torrents").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Test]
    public async Task HandleRpc_Multicall_AuthLoginFailed_SubsequentCallsReturnUnauthenticated()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("[{\"method\":\"auth.login\",\"params\":[\"wrong_key\"],\"id\":1},{\"method\":\"core.get_config\",\"params\":[],\"id\":2}]");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);

        using var responseDoc = JsonDocument.Parse(json);
        responseDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        responseDoc.RootElement.GetArrayLength().Should().Be(2);

        var first = responseDoc.RootElement[0];
        first.GetProperty("result").GetBoolean().Should().BeFalse();

        var second = responseDoc.RootElement[1];
        second.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);
        second.GetProperty("error").GetProperty("message").GetString().Should().Be("Not authenticated");
    }

    [Test]
    public async Task HandleRpc_CorePauseAllTorrents_PausesAllTorrents()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var t1 = new Torrent { Id = 1, InfoHash = "hash1" };
        var t2 = new Torrent { Id = 2, InfoHash = "hash2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2 });

        using var doc = JsonDocument.Parse("{\"method\":\"core.pause_all_torrents\",\"params\":[],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        await this.torrentService.Received(1).PauseAsync(1);
        await this.torrentService.Received(1).PauseAsync(2);
    }

    [Test]
    public async Task HandleRpc_CoreResumeAllTorrents_ResumesAllTorrents()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var t1 = new Torrent { Id = 1, InfoHash = "hash1" };
        var t2 = new Torrent { Id = 2, InfoHash = "hash2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2 });

        using var doc = JsonDocument.Parse("{\"method\":\"core.resume_all_torrents\",\"params\":[],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        await this.torrentService.Received(1).ResumeAsync(1);
        await this.torrentService.Received(1).ResumeAsync(2);
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentsStatus_IncludesPiecesKey()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "aabb11223344556677889900aabb112233445566",
            Name = "Pieces Test",
            PieceCount = 4,
            PieceLength = 262144,
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.PieceBitfield.Returns(new[] { true, false, true, false });
        this.torrentService.GetDownloadTask(10).Returns(mockTask);

        using var doc = JsonDocument.Parse("{\"method\":\"core.get_torrents_status\",\"params\":[{},[\"pieces\",\"num_pieces\",\"piece_length\",\"storage_mode\",\"move_completed_path\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        using var respDoc = JsonDocument.Parse(json);
        var torrents = respDoc.RootElement.GetProperty("result");
        var tStatus = torrents.GetProperty("aabb11223344556677889900aabb112233445566");

        tStatus.GetProperty("num_pieces").GetInt32().Should().Be(4);
        tStatus.GetProperty("piece_length").GetInt32().Should().Be(262144);
        tStatus.GetProperty("storage_mode").GetString().Should().Be("sparse");
        var pieces = tStatus.GetProperty("pieces").EnumerateArray().Select(x => x.GetInt32()).ToList();
        pieces.Should().Equal(1, 0, 1, 0);
    }

    [Test]
    public async Task HandleRpc_CoreQueueTop_MovesTorrentsInReverseOrderToPreserveBatchOrdering()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tA = new Torrent { Id = 1, InfoHash = "hashA", QueuePosition = 3 };
        var tB = new Torrent { Id = 2, InfoHash = "hashB", QueuePosition = 4 };
        this.torrentService.GetByInfoHash("hashA").Returns(tA);
        this.torrentService.GetByInfoHash("hashB").Returns(tB);

        using var doc = JsonDocument.Parse("{\"method\":\"core.queue_top\",\"params\":[[\"hashA\",\"hashB\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        Received.InOrder(() =>
        {
            this.torrentService.MoveQueueAsync(2, "top");
            this.torrentService.MoveQueueAsync(1, "top");
        });
    }

    [Test]
    public async Task HandleRpc_CoreQueueBottom_MovesTorrentsInNaturalOrder()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tA = new Torrent { Id = 1, InfoHash = "hashA", QueuePosition = 1 };
        var tB = new Torrent { Id = 2, InfoHash = "hashB", QueuePosition = 2 };
        this.torrentService.GetByInfoHash("hashA").Returns(tA);
        this.torrentService.GetByInfoHash("hashB").Returns(tB);

        using var doc = JsonDocument.Parse("{\"method\":\"core.queue_bottom\",\"params\":[[\"hashA\",\"hashB\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        Received.InOrder(() =>
        {
            this.torrentService.MoveQueueAsync(1, "bottom");
            this.torrentService.MoveQueueAsync(2, "bottom");
        });
    }

    [Test]
    public async Task HandleRpc_CoreQueueUp_SortsTorrentsByQueuePositionAscending()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tA = new Torrent { Id = 1, InfoHash = "hashA", QueuePosition = 5 };
        var tB = new Torrent { Id = 2, InfoHash = "hashB", QueuePosition = 2 };
        var tC = new Torrent { Id = 3, InfoHash = "hashC", QueuePosition = 4 };
        this.torrentService.GetByInfoHash("hashA").Returns(tA);
        this.torrentService.GetByInfoHash("hashB").Returns(tB);
        this.torrentService.GetByInfoHash("hashC").Returns(tC);

        using var doc = JsonDocument.Parse("{\"method\":\"core.queue_up\",\"params\":[[\"hashA\",\"hashB\",\"hashC\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        Received.InOrder(() =>
        {
            this.torrentService.MoveQueueAsync(2, "up");
            this.torrentService.MoveQueueAsync(3, "up");
            this.torrentService.MoveQueueAsync(1, "up");
        });
    }

    [Test]
    public async Task HandleRpc_CoreQueueDown_SortsTorrentsByQueuePositionDescending()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "deluge_secret_key";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tA = new Torrent { Id = 1, InfoHash = "hashA", QueuePosition = 5 };
        var tB = new Torrent { Id = 2, InfoHash = "hashB", QueuePosition = 2 };
        var tC = new Torrent { Id = 3, InfoHash = "hashC", QueuePosition = 4 };
        this.torrentService.GetByInfoHash("hashA").Returns(tA);
        this.torrentService.GetByInfoHash("hashB").Returns(tB);
        this.torrentService.GetByInfoHash("hashC").Returns(tC);

        using var doc = JsonDocument.Parse("{\"method\":\"core.queue_down\",\"params\":[[\"hashB\",\"hashA\",\"hashC\"]],\"id\":1}");
        var result = await this.controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        Received.InOrder(() =>
        {
            this.torrentService.MoveQueueAsync(1, "down");
            this.torrentService.MoveQueueAsync(3, "down");
            this.torrentService.MoveQueueAsync(2, "down");
        });
    }
}
