// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Flood;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class FloodApiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private FloodApiController controller = null!;

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

        this.controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            configFileProvider: this.configFileProvider);
    }

    [Test]
    public void GetTorrents_WhenTorrentStoppedIncomplete_ReturnsStoppedStatus()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "IncompleteTorrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Status = TorrentStatus.Stopped,
            Progress = 0.5,
            DateCompleted = null,
            IsPrivate = true,
            InitialSeeding = true,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = this.controller.GetTorrents();
        actionResult.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var tObj = doc.RootElement.GetProperty("torrents").GetProperty("aabbccddeeff00112233445566778899aabbccdd");

        var statusElem = tObj.GetProperty("status");
        statusElem.GetArrayLength().Should().Be(1);
        statusElem[0].GetString().Should().Be("stopped");

        tObj.GetProperty("isPrivate").GetBoolean().Should().BeTrue();
        tObj.GetProperty("isInitialSeeding").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void GetTorrents_WhenTorrentStoppedComplete_ReturnsCompleteStatus()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "CompletedTorrent",
            InfoHash = "1122334455667788990011223344556677889900",
            Status = TorrentStatus.Stopped,
            Progress = 1.0,
            DateCompleted = DateTime.UtcNow,
            IsPrivate = false,
            InitialSeeding = false,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = this.controller.GetTorrents();
        actionResult.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var tObj = doc.RootElement.GetProperty("torrents").GetProperty("1122334455667788990011223344556677889900");

        var statusElem = tObj.GetProperty("status");
        statusElem.GetArrayLength().Should().Be(1);
        statusElem[0].GetString().Should().Be("complete");

        tObj.GetProperty("isPrivate").GetBoolean().Should().BeFalse();
        tObj.GetProperty("isInitialSeeding").GetBoolean().Should().BeFalse();
    }
}
