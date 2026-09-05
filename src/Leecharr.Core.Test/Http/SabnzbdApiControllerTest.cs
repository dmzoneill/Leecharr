// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Sabnzbd;
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
public class SabnzbdApiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IDiskProvider diskProvider = null!;
    private SabnzbdApiController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.diskProvider = Substitute.For<IDiskProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new SabnzbdApiController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            diskProvider: this.diskProvider);
    }

    [Test]
    public async Task HandleApi_Queue_ReturnsFreeAndTotalDiskSpaceFromDiskProvider()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.configService.IncompleteDownloadDir.Returns("/incomplete");
        this.torrentService.GetAll().Returns(new List<Torrent>());

        // 1 TB = 1073741824000 bytes -> 1000.00 GB
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(1073741824000L);
        // 2 TB = 2147483648000 bytes -> 2000.00 GB
        this.diskProvider.GetTotalSize(Arg.Any<string>()).Returns(2147483648000L);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var queue = doc.RootElement.GetProperty("queue");
        queue.GetProperty("diskspace1").GetString().Should().Be("1000.00");
        queue.GetProperty("diskspace2").GetString().Should().Be("1000.00");
        queue.GetProperty("diskspacetotal1").GetString().Should().Be("2000.00");
        queue.GetProperty("diskspacetotal2").GetString().Should().Be("2000.00");
    }

    [Test]
    public async Task HandleApi_Version_ReturnsVersion()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleApi(
            mode: "version",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("version").GetString().Should().Be("4.3.2");
    }
}
