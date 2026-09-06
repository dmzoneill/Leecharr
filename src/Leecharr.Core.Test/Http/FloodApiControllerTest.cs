// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Leecharr.Api.V1.Flood;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
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
    private IUserService userService = null!;
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
        this.userService = Substitute.For<IUserService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.userService);
    }

    [Test]
    public void GetTorrents_MapsStoppedIncompleteAsStopped_AndCompleteAsComplete_AndMapsIsPrivateAndInitialSeeding()
    {
        var incompleteStopped = new Torrent
        {
            Id = 1,
            InfoHash = "1111111111111111111111111111111111111111",
            Name = "Incomplete Torrent",
            Status = TorrentStatus.Stopped,
            Progress = 0.5,
            IsPrivate = true,
            InitialSeeding = true,
        };

        var completedStopped = new Torrent
        {
            Id = 2,
            InfoHash = "2222222222222222222222222222222222222222",
            Name = "Complete Torrent",
            Status = TorrentStatus.Stopped,
            Progress = 1.0,
            DateCompleted = DateTime.UtcNow,
            IsPrivate = false,
            InitialSeeding = false,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { incompleteStopped, completedStopped });

        var result = this.controller.GetTorrents();
        result.Should().BeOfType<OkObjectResult>();

        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var torrents = doc.RootElement.GetProperty("torrents");

        var t1 = torrents.GetProperty("1111111111111111111111111111111111111111");
        t1.GetProperty("status")[0].GetString().Should().Be("stopped");
        t1.GetProperty("isPrivate").GetBoolean().Should().BeTrue();
        t1.GetProperty("isInitialSeeding").GetBoolean().Should().BeTrue();

        var t2 = torrents.GetProperty("2222222222222222222222222222222222222222");
        t2.GetProperty("status")[0].GetString().Should().Be("complete");
        t2.GetProperty("isPrivate").GetBoolean().Should().BeFalse();
        t2.GetProperty("isInitialSeeding").GetBoolean().Should().BeFalse();
    }
}
