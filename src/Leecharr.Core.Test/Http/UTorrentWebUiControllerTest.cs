// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.UTorrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class UTorrentWebUiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private UTorrentWebUiController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new UTorrentWebUiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            configFileProvider: this.configFileProvider);
    }

    [TestCase("removedata")]
    [TestCase("remove")]
    [TestCase("add-url")]
    [TestCase("start")]
    [TestCase("stop")]
    [TestCase("pause")]
    [TestCase("recheck")]
    [TestCase("setprops")]
    public async Task HandleWebUi_RejectsMutatingActionsViaGet(string action)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.HandleWebUi(null!, action, "hash123", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result;
        objectResult.StatusCode.Should().Be(StatusCodes.Status405MethodNotAllowed);
    }

    [TestCase("getprops")]
    [TestCase("getsettings")]
    [TestCase("getfiles")]
    public async Task HandleWebUi_AllowsQueryActionsViaGet(string action)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.HandleWebUi(null!, action, "hash123", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task HandleWebUi_AllowsMutatingActionsViaPost()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 42, InfoHash = "abc12345" };
        this.torrentService.GetByInfoHash("abc12345").Returns(torrent);

        var result = await this.controller.HandleWebUi(null!, "removedata", "abc12345", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(42, true);
    }

    [Test]
    public async Task HandleWebUi_AddUrl_FallsBackToPathInForm_WhenDownloadDirMissing()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new System.Collections.Generic.Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            { "path", "/downloads/custom" },
            { "label", "tv-shows" },
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
        var result = await this.controller.HandleWebUi(null!, "add-url", null!, magnet, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnet, "tv-shows", "/downloads/custom", false);
    }

    [Test]
    public async Task HandleWebUi_SetPrio_MapsUTorrentPrioritiesToInternal()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 42, InfoHash = "utprio123" };
        var files = new List<TorrentFile>
        {
            new TorrentFile { Id = 1, TorrentId = 42, Path = "f0.mkv" },
            new TorrentFile { Id = 2, TorrentId = 42, Path = "f1.mkv" },
            new TorrentFile { Id = 3, TorrentId = 42, Path = "f2.mkv" },
            new TorrentFile { Id = 4, TorrentId = 42, Path = "f3.mkv" },
        };

        this.torrentService.GetByInfoHash("utprio123").Returns(torrent);
        this.torrentFileService.GetFiles(42).Returns(files);

        await this.controller.HandleWebUi(null!, "setprio", "utprio123", p: "0", f: "0", null!, null!, null!, null!);
        await this.torrentFileService.Received(1).SetPriorityAsync(1, 0); // 0 -> 0

        await this.controller.HandleWebUi(null!, "setprio", "utprio123", p: "1", f: "1", null!, null!, null!, null!);
        await this.torrentFileService.Received(1).SetPriorityAsync(2, 1); // 1 -> 1

        await this.controller.HandleWebUi(null!, "setprio", "utprio123", p: "2", f: "2", null!, null!, null!, null!);
        await this.torrentFileService.Received(1).SetPriorityAsync(3, 3); // 2 -> 3 (Normal)

        await this.controller.HandleWebUi(null!, "setprio", "utprio123", p: "3", f: "3", null!, null!, null!, null!);
        await this.torrentFileService.Received(1).SetPriorityAsync(4, 4); // 3 -> 4 (High)
    }

    [Test]
    public async Task HandleWebUi_GetFiles_MapsInternalPrioritiesToUTorrentProtocol()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 42, InfoHash = "utprio123" };
        var files = new List<TorrentFile>
        {
            new TorrentFile { Id = 1, TorrentId = 42, Path = "f0.mkv", Priority = 0, Size = 100, BytesCompleted = 0 },
            new TorrentFile { Id = 2, TorrentId = 42, Path = "f1.mkv", Priority = 1, Size = 100, BytesCompleted = 50 },
            new TorrentFile { Id = 3, TorrentId = 42, Path = "f2.mkv", Priority = 3, Size = 100, BytesCompleted = 100 },
            new TorrentFile { Id = 4, TorrentId = 42, Path = "f3.mkv", Priority = 4, Size = 100, BytesCompleted = 100 },
        };

        this.torrentService.GetByInfoHash("utprio123").Returns(torrent);
        this.torrentFileService.GetFiles(42).Returns(files);

        var result = await this.controller.HandleWebUi(null!, "getfiles", "utprio123", null!, null!, null!, null!, null!, null!);
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;

        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        json.Should().Contain("[\"f0.mkv\",100,0,0]");
        json.Should().Contain("[\"f1.mkv\",100,50,1]");
        json.Should().Contain("[\"f2.mkv\",100,100,2]");
        json.Should().Contain("[\"f3.mkv\",100,100,3]");
    }
}
