// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Deluge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
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

        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("deluge_secret_key");

        this.controller = new DelugeJsonRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);
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
}
