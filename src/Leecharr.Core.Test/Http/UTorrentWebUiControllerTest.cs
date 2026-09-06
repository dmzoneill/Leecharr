// Copyright (c) PlaceholderCompany. All rights reserved.

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
    public async Task SetPrio_MapsUTorrentPrioritiesToLeecharrInternal()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?action=setprio&hash=UTORRENTHASH&p=2&f=0");
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 1, InfoHash = "UTORRENTHASH", Name = "Test" };
        this.torrentService.GetByInfoHash("UTORRENTHASH").Returns(torrent);
        var files = new System.Collections.Generic.List<TorrentFile>
        {
            new() { Id = 100, TorrentId = 1, Path = "f1" },
        };
        this.torrentFileService.GetFiles(1).Returns(files);

        var result = await this.controller.HandleWebUi();
        result.Should().BeOfType<OkObjectResult>();

        await this.torrentFileService.Received(1).SetPriorityAsync(100, 3);
    }
}
