// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Aria2;
using Leecharr.Api.V1.Freebox;
using Leecharr.Api.V1.Hadouken;
using Leecharr.Api.V1.Nzbget;
using Leecharr.Api.V1.NzbVortex;
using Leecharr.Api.V1.RTorrent;
using Leecharr.Api.V1.Sabnzbd;
using Leecharr.Api.V1.Synology;
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
public class CompatibilityRpcAuthTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;

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
        this.configFileProvider.ApiKey.Returns("master_api_key_xyz");
    }

    [Test]
    public async Task UTorrentWebUi_WhenUnauthenticated_Returns401()
    {
        var controller = new UTorrentWebUiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            configFileProvider: this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tokenResult = controller.GetToken();
        tokenResult.Should().BeOfType<UnauthorizedResult>();

        var webUiResult = await controller.HandleWebUi(null!, "getprops", "hash1", null!, null!, null!, null!, null!, null!);
        webUiResult.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task RTorrent_WhenUnauthenticated_Returns401()
    {
        var controller = new RTorrentController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.torrentFileService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleXmlRpc();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task Sabnzbd_WhenUnauthenticated_Returns401()
    {
        var controller = new SabnzbdApiController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleApi("queue", null!, null!, null!, null!, null!);
        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task Nzbget_WhenUnauthenticated_Returns401()
    {
        var controller = new NzbgetRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleRpc(new NzbgetRequest { Method = "listgroups" });
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void Synology_WhenUnauthenticated_Returns401()
    {
        var controller = new SynologyDownloadStationController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var infoResult = controller.Info("getInfo");
        infoResult.Should().BeOfType<UnauthorizedResult>();

        var statResult = controller.Statistic("getStatistic");
        statResult.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task Hadouken_WhenUnauthenticated_Returns401()
    {
        var controller = new HadoukenRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.torrentFileService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleRpc(new HadoukenRpcRequest { Method = "webui.list" });
        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public void Freebox_WhenUnauthenticated_Returns401()
    {
        var controller = new FreeboxDownloadController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetDownloads();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void NzbVortex_WhenUnauthenticated_Returns401()
    {
        var controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetAppVersion();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void NzbVortex_WhenHardcodedSessionTokenProvided_Returns401()
    {
        var controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?session=leecharr-session-token");
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetAppVersion();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void NzbVortex_WhenAuthenticatedViaLogin_SessionTokenGrantsAccess()
    {
        var controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var loginContext = new DefaultHttpContext();
        loginContext.Request.Headers["X-Api-Key"] = "master_api_key_xyz";
        controller.ControllerContext = new ControllerContext { HttpContext = loginContext };

        var loginResult = controller.Login();
        loginResult.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)loginResult;
        var sessionProp = okResult.Value!.GetType().GetProperty("session")!.GetValue(okResult.Value)!.ToString();
        sessionProp.Should().NotBeNullOrWhiteSpace();
        sessionProp.Should().NotBe("leecharr-session-token");

        var authedContext = new DefaultHttpContext();
        authedContext.Request.QueryString = new QueryString($"?session={sessionProp}");
        controller.ControllerContext = new ControllerContext { HttpContext = authedContext };

        var appVersionResult = controller.GetAppVersion();
        appVersionResult.Should().BeOfType<OkObjectResult>();
    }
}
