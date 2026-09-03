// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.QBittorrent;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class QBittorrentApiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IConfigFileProvider configFileProvider = null!;
    private QBittorrentApiController controller = null!;

    [SetUp]
    public void SetUp()
    {
        QBittorrentApiController.ResetSyncState();

        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);
        this.categoryService.GetAll().Returns(new List<Category>());

        this.controller = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            configFileProvider: this.configFileProvider);
    }

    [Test]
    public void GetMainData_WithRidZero_ReturnsFullUpdate()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.2,
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Name = "Torrent 2",
            InfoHash = "hash2",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var actionResult = this.controller.GetMainData(0);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var data = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        data["full_update"].Should().Be(true);
        data["rid"].Should().Be(1);

        var torrents = data["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        torrents.Count.Should().Be(2);
        torrents.Contains("hash1").Should().BeTrue();
        torrents.Contains("hash2").Should().BeTrue();
    }

    [Test]
    public void GetMainData_WithSubsequentRid_WhenUnchanged_ReturnsIncrementalUpdateWithEmptyTorrents()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.2,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1 });

        // Initial full sync
        var initial = this.controller.GetMainData(0);
        var initialData = ((OkObjectResult)initial.Result!).Value as Dictionary<string, object>;
        initialData!["full_update"].Should().Be(true);
        var initialRid = (int)initialData["rid"];

        // Subsequent incremental sync
        var delta = this.controller.GetMainData(initialRid);
        var deltaResult = delta.Result.Should().BeOfType<OkObjectResult>().Subject;
        var deltaData = deltaResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        deltaData["full_update"].Should().Be(false);
        deltaData["rid"].Should().Be(initialRid + 1);

        var updatedTorrents = deltaData["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        updatedTorrents.Count.Should().Be(0);
    }

    [Test]
    public void GetMainData_WithSubsequentRid_WhenTorrentModified_ReturnsOnlyModifiedTorrent()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.2,
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Name = "Torrent 2",
            InfoHash = "hash2",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        // Initial full sync
        var initial = this.controller.GetMainData(0);
        var initialData = ((OkObjectResult)initial.Result!).Value as Dictionary<string, object>;
        var initialRid = (int)initialData!["rid"];

        // Update torrent 1
        var modifiedTorrent1 = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.75,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { modifiedTorrent1, torrent2 });

        // Subsequent sync
        var delta = this.controller.GetMainData(initialRid);
        var deltaData = ((OkObjectResult)delta.Result!).Value as Dictionary<string, object>;

        deltaData!["full_update"].Should().Be(false);
        var updatedTorrents = deltaData["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        updatedTorrents.Count.Should().Be(1);
        updatedTorrents.Contains("hash1").Should().BeTrue();
        updatedTorrents.Contains("hash2").Should().BeFalse();
    }

    [Test]
    public void GetMainData_WithSubsequentRid_WhenTorrentRemoved_ReturnsInTorrentsRemoved()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.2,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1 });

        // Initial full sync
        var initial = this.controller.GetMainData(0);
        var initialData = ((OkObjectResult)initial.Result!).Value as Dictionary<string, object>;
        var initialRid = (int)initialData!["rid"];

        // Remove torrent1
        this.torrentService.GetAll().Returns(new List<Torrent>());

        // Subsequent sync
        var delta = this.controller.GetMainData(initialRid);
        var deltaData = ((OkObjectResult)delta.Result!).Value as Dictionary<string, object>;

        deltaData!["full_update"].Should().Be(false);
        var removed = deltaData["torrents_removed"].Should().BeAssignableTo<IEnumerable<string>>().Subject;
        removed.Should().Contain("hash1");
    }
}
