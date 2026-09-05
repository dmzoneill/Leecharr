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
}
