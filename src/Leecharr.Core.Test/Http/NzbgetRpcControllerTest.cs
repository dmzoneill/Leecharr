// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Nzbget;
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
public class NzbgetRpcControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IDiskProvider diskProvider = null!;
    private NzbgetRpcController controller = null!;

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

        this.controller = new NzbgetRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.diskProvider);
    }

    [Test]
    public async Task HandleRpc_Status_ReturnsFreeDiskSpaceMBFromDiskProvider()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.torrentService.GetAll().Returns(new List<Torrent>());
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(524288000000L);

        var request = new NzbgetRequest
        {
            Method = "status",
            Id = 1,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var resObj = doc.RootElement.GetProperty("result");
        resObj.GetProperty("FreeDiskSpaceMB").GetInt32().Should().Be((int)(524288000000L / (1024 * 1024)));
    }

    [Test]
    public async Task HandleRpc_Version_ReturnsVersion()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var request = new NzbgetRequest
        {
            Method = "version",
            Id = 2,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result").GetString().Should().Be("24.0");
    }
}
