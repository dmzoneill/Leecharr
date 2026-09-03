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
}
