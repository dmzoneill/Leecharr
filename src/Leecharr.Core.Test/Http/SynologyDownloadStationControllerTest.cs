// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Synology;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class SynologyDownloadStationControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private SynologyDownloadStationController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new SynologyDownloadStationController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.safeHttpClientService);
    }

    [Test]
    public async Task HandleTask_Create_FallsBackToUrlInForm_WhenUriMissing()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "url", magnet },
            { "destination", "/volume1/downloads" },
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "create",
            id: null!,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnet, null, "/volume1/downloads", false);
    }

    [Test]
    public async Task HandleTask_Create_PrefersUriInForm_WhenBothUriAndUrlPresent()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        var magnetUri = "magnet:?xt=urn:btih:1111111111111111111111111111111111111111";
        var magnetUrl = "magnet:?xt=urn:btih:2222222222222222222222222222222222222222";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "uri", magnetUri },
            { "url", magnetUrl },
            { "destination", "/downloads" },
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "create",
            id: null!,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, null, "/downloads", false);
    }
}
