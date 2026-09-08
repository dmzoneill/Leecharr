// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.QBittorrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
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
    private IDiskProvider diskProvider = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
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
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);
        this.categoryService.GetAll().Returns(new List<Category>());

        this.controller = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            configFileProvider: this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService,
            diskProvider: this.diskProvider);
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

    [Test]
    public async Task SetLocation_WithValidHashesAndLocation_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "QBit Torrent",
            InfoHash = "hash1",
        };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = await this.controller.SetLocation("hash1", "/downloads/moved");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).SetLocationAsync(10, "/downloads/moved", moveFiles: true);
    }

    [Test]
    public async Task SetLocation_WithMultipleHashes_InvokesSetLocationAsyncForEachTorrent()
    {
        var torrent1 = new Torrent { Id = 10, Name = "Torrent 1", InfoHash = "hash1" };
        var torrent2 = new Torrent { Id = 20, Name = "Torrent 2", InfoHash = "hash2" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);
        this.torrentService.GetByInfoHash("hash2").Returns(torrent2);

        var result = await this.controller.SetLocation("hash1|hash2", "/downloads/moved");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).SetLocationAsync(10, "/downloads/moved", moveFiles: true);
        await this.torrentService.Received(1).SetLocationAsync(20, "/downloads/moved", moveFiles: true);
    }

    [Test]
    public void GetFiles_ReturnsEnrichedProgressAndIsSeed()
    {
        var torrent = new Torrent
        {
            Id = 5,
            InfoHash = "qbhash",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            PieceLength = 512,
            PieceCount = 4,
            TotalSize = 2048,
        };

        var task = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadTask>();
        task.PieceBitfield.Returns(new[] { true, true, false, false });
        task.PieceLength.Returns(512);

        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 5, Path = "file1.dat", Size = 1024, PieceOffset = 0, PieceCount = 2, Progress = 0.0 },
            new() { Id = 2, TorrentId = 5, Path = "file2.dat", Size = 1024, PieceOffset = 2, PieceCount = 2, Progress = 0.0 },
        };

        this.torrentService.GetByInfoHash("qbhash").Returns(torrent);
        this.torrentService.GetDownloadTask(5).Returns(task);
        this.torrentFileService.GetFiles(5).Returns(files);

        var response = this.controller.GetFiles("qbhash");

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(2);

        list[0]["name"].Should().Be("file1.dat");
        list[0]["progress"].Should().Be(1.0);
        list[0]["is_seed"].Should().Be(true);

        list[1]["name"].Should().Be("file2.dat");
        list[1]["progress"].Should().Be(0.0);
        list[1]["is_seed"].Should().Be(false);
    }

    [Test]
    public void GetTorrentsInfo_WithHashesAll_ReturnsAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var response = this.controller.GetTorrentsInfo(hashes: "all");

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(2);
    }

    [Test]
    public async Task PauseTorrents_WithHashesAll_PausesAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.PauseTorrents("all");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).PauseAsync(1);
        await this.torrentService.Received(1).PauseAsync(2);
    }

    [Test]
    public async Task ResumeTorrents_WithHashesAll_ResumesAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.ResumeTorrents("all");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).ResumeAsync(1);
        await this.torrentService.Received(1).ResumeAsync(2);
    }

    [Test]
    public async Task DeleteTorrents_WithHashesAll_DeletesAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.DeleteTorrents("all", deleteFiles: true);

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).DeleteAsync(1, true);
        await this.torrentService.Received(1).DeleteAsync(2, true);
    }

    [Test]
    public async Task RecheckTorrents_WithHashesAll_RechecksAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.RecheckTorrents("all");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).ForceRecheckAsync(1);
        await this.torrentService.Received(1).ForceRecheckAsync(2);
    }

    [Test]
    public async Task SetCategory_WithHashesAll_SetsCategoryOnAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.SetCategory("all", "movies");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).SetCategoryAsync(1, "movies");
        await this.torrentService.Received(1).SetCategoryAsync(2, "movies");
    }

    [Test]
    public async Task SetForceStart_WithHashesAll_SetsForceStartOnAllTorrentsAndResumes()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.SetForceStart("all", "true");

        result.Should().BeOfType<ContentResult>();
        torrent1.ForceStart.Should().BeTrue();
        torrent2.ForceStart.Should().BeTrue();
        await this.torrentService.Received(1).UpdateAsync(torrent1);
        await this.torrentService.Received(1).UpdateAsync(torrent2);
        await this.torrentService.Received(1).ResumeAsync(1);
        await this.torrentService.Received(1).ResumeAsync(2);
    }

    [Test]
    public async Task SetForceStart_WithForceFalse_UpdatesFlagWithoutCallingResume()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", ForceStart = true };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1 });

        var result = await this.controller.SetForceStart("hash1", "false");

        result.Should().BeOfType<ContentResult>();
        torrent1.ForceStart.Should().BeFalse();
        await this.torrentService.Received(1).UpdateAsync(torrent1);
        await this.torrentService.DidNotReceive().ResumeAsync(Arg.Any<int>());
    }

    [Test]
    public async Task SetSuperSeeding_WithHashesAll_SetsSuperSeedingOnAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.SetSuperSeeding("all", true);

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).SetSuperSeedingAsync(1, true);
        await this.torrentService.Received(1).SetSuperSeedingAsync(2, true);
    }

    [Test]
    public async Task AddAndRemoveTags_WithHashesAll_PreservesAndMergesTagsOnAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", Label = "oldTag" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2", Label = "oldTag" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var addResult = await this.controller.AddTags("all", "tag1, tag2");
        addResult.Should().BeOfType<ContentResult>();
        torrent1.Label.Should().Be("oldTag, tag1, tag2");
        torrent2.Label.Should().Be("oldTag, tag1, tag2");

        var removeResult = await this.controller.RemoveTags("all", "tag1");
        removeResult.Should().BeOfType<ContentResult>();
        torrent1.Label.Should().Be("oldTag, tag2");
        torrent2.Label.Should().Be("oldTag, tag2");
    }

    [Test]
    public async Task AddTorrents_WithSequentialDownloadTrue_SetsSequentialDownload()
    {
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(
            urls: "magnet:?xt=urn:btih:hash1",
            sequentialDownload: "true",
            firstLastPiecePrio: "false");

        result.Should().BeOfType<ContentResult>();
        addedTorrent.SequentialDownload.Should().BeTrue();
        await this.torrentService.Received(1).UpdateAsync(addedTorrent);
    }

    [Test]
    public async Task AddTorrents_WithFirstLastPiecePrioOnly_DoesNotSetSequentialDownload()
    {
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", SequentialDownload = false };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(
            urls: "magnet:?xt=urn:btih:hash1",
            sequentialDownload: "false",
            firstLastPiecePrio: "true");

        result.Should().BeOfType<ContentResult>();
        addedTorrent.SequentialDownload.Should().BeFalse();
    }

    [Test]
    public async Task PriorityAndLimits_WithHashesAll_AppliesToAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        await this.controller.TopPrio("all");
        await this.torrentService.Received(1).MoveQueueAsync(1, "top");
        await this.torrentService.Received(1).MoveQueueAsync(2, "top");

        await this.controller.BottomPrio("all");
        await this.torrentService.Received(1).MoveQueueAsync(1, "bottom");
        await this.torrentService.Received(1).MoveQueueAsync(2, "bottom");

        await this.controller.IncreasePrio("all");
        await this.torrentService.Received(1).MoveQueueAsync(1, "up");
        await this.torrentService.Received(1).MoveQueueAsync(2, "up");

        await this.controller.DecreasePrio("all");
        await this.torrentService.Received(1).MoveQueueAsync(1, "down");
        await this.torrentService.Received(1).MoveQueueAsync(2, "down");

        await this.controller.SetTorrentDownloadLimit("all", 1048576);
        torrent1.DownloadLimit.Should().Be(1024);
        torrent2.DownloadLimit.Should().Be(1024);

        await this.controller.SetTorrentUploadLimit("all", 524288);
        torrent1.UploadLimit.Should().Be(512);
        torrent2.UploadLimit.Should().Be(512);

        await this.controller.SetShareLimits("all", ratioLimit: 2.0, seedingTimeLimit: 7200, maxRatioAction: 1);
        torrent1.TargetRatio.Should().Be(2.0);
        torrent1.TargetSeedTimeMinutes.Should().Be(120);
        torrent1.ShareLimitAction.Should().Be("Remove");
    }

    [Test]
    public void GetTorrentList_ReturnsSeedingTimeLimitInSeconds()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "T1",
            TargetSeedTimeMinutes = 60,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = this.controller.GetTorrentsInfo();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject.ToList();

        list.Should().HaveCount(1);
        list[0]["seeding_time_limit"].Should().Be(3600);
        list[0]["max_seeding_time"].Should().Be(3600);
    }

    [Test]
    public void GetTorrentPeers_WithValidHash_ReturnsSwarmPeers()
    {
        var downloadEngine = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadEngine>();
        var downloadTask = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadTask>();
        var peers = new List<NzbDrone.Core.BitTorrent.PeerInfo>
        {
            new()
            {
                Ip = "192.168.1.50",
                Port = 6881,
                Client = "Leecharr/1.0",
                Flags = "uI",
                Progress = 0.75,
                DownloadSpeed = 1048576,
                UploadSpeed = 524288,
                Downloaded = 100000000,
                Uploaded = 50000000,
            },
        };
        downloadTask.GetPeers().Returns(peers);
        downloadEngine.GetTask(1).Returns(downloadTask);

        var controllerWithEngine = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine);

        var torrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = controllerWithEngine.GetTorrentPeers("hash1", rid: 5);
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var data = okResult.Value;

        var fullUpdate = (bool)data!.GetType().GetProperty("full_update")!.GetValue(data)!;
        var rid = (int)data!.GetType().GetProperty("rid")!.GetValue(data)!;
        var peerDict = (Dictionary<string, object>)data!.GetType().GetProperty("peers")!.GetValue(data)!;

        fullUpdate.Should().BeTrue();
        rid.Should().Be(6);
        peerDict.Should().ContainKey("192.168.1.50:6881");
    }

    [Test]
    public void GetPieceStates_WithValidHash_ReturnsMappedPieceStates()
    {
        var downloadEngine = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadEngine>();
        var downloadTask = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadTask>();
        downloadTask.PieceBitfield.Returns(new[] { true, false, true, true });
        downloadEngine.GetTask(1).Returns(downloadTask);

        var controllerWithEngine = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine);

        var torrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = controllerWithEngine.GetPieceStates("hash1");
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var states = okResult.Value.Should().BeOfType<List<int>>().Subject;

        states.Should().Equal(2, 0, 2, 2);
    }

    [Test]
    public void GetProperties_ReturnsCompletePropertySet()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "T1",
            CreatedBy = "Leecharr",
            DownloadSpeed = 1048576,
            UploadSpeed = 524288,
            Eta = 300,
            Seeders = 5,
            Leechers = 10,
            TotalSize = 1000000000,
            PieceLength = 262144,
            PieceCount = 3815,
            Progress = 0.5,
            Downloaded = 500000000,
            Uploaded = 250000000,
        };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var actionResult = this.controller.GetProperties("hash1");
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dict = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        dict.Should().ContainKey("addition_date");
        dict.Should().ContainKey("completion_date");
        dict.Should().ContainKey("created_by");
        dict.Should().ContainKey("dl_speed");
        dict.Should().ContainKey("up_speed");
        dict.Should().ContainKey("eta");
        dict.Should().ContainKey("peers");
        dict.Should().ContainKey("seeds");
        dict.Should().ContainKey("total_size");

        dict["dl_speed"].Should().Be(1048576L);
        dict["up_speed"].Should().Be(524288L);
        dict["eta"].Should().Be(300L);
        dict["seeds"].Should().Be(5);
        dict["peers"].Should().Be(10);
        dict["total_size"].Should().Be(1000000000L);
    }

    [Test]
    public void SetPreferences_UpdatesConfigService()
    {
        var json = "{\"dl_limit\":10485760,\"up_limit\":5242880,\"dht\":true,\"pex\":true,\"save_path\":\"/data/downloads\"}";
        var result = this.controller.SetPreferences(json);
        result.Should().BeOfType<ContentResult>();

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["MaxDownloadSpeedKbps"] == 10240 &&
            (int)d["MaxUploadSpeedKbps"] == 5120 &&
            (bool)d["EnableDht"] == true &&
            (bool)d["EnablePex"] == true &&
            (string)d["DownloadDir"] == "/data/downloads"));
    }

    [Test]
    public async Task SpeedLimitsMode_GetAndToggle_UpdatesStateAndCallsSpeedSchedulerService()
    {
        this.configService.AlternativeSpeedEnabled.Returns(false);
        this.configService.AltDownloadSpeedKbps.Returns(500);
        this.configService.AltUploadSpeedKbps.Returns(100);

        var scheduler = Substitute.For<ISpeedSchedulerService>();
        var controllerWithScheduler = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            speedSchedulerService: scheduler);

        var getResult = controllerWithScheduler.GetSpeedLimitsMode();
        var okGet = getResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        okGet.Value.Should().Be(0);

        var toggleResult = await controllerWithScheduler.ToggleSpeedLimitsMode();
        toggleResult.Should().BeOfType<ContentResult>();

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["AlternativeSpeedEnabled"] == true));
        await scheduler.Received(1).ApplyCurrentLimitsAsync();
    }

    [Test]
    public async Task SpeedLimitsMode_SetMode_UpdatesStateAndCallsSpeedSchedulerService()
    {
        var scheduler = Substitute.For<ISpeedSchedulerService>();
        var controllerWithScheduler = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            speedSchedulerService: scheduler);

        var setResult = await controllerWithScheduler.SetSpeedLimitsMode(1);
        setResult.Should().BeOfType<ContentResult>();

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["AlternativeSpeedEnabled"] == true));
        await scheduler.Received(1).ApplyCurrentLimitsAsync();
    }

    [Test]
    public async Task EditTracker_WhenValid_UpdatesTrackerInRepositoryAndEngine()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var tracker = new TrackerEntry { Id = 10, TorrentId = 1, Url = "http://tracker1.org/announce" };
        this.trackerEntryRepository.GetByTorrentId(1).Returns(new List<TrackerEntry> { tracker });

        var downloadEngine = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadEngine>();
        var ctrl = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine,
            configFileProvider: this.configFileProvider);

        var result = await ctrl.EditTracker("hash1", "http://tracker1.org/announce", "http://tracker2.org/announce");

        result.Should().BeOfType<ContentResult>();
        tracker.Url.Should().Be("http://tracker2.org/announce");
        this.trackerEntryRepository.Received(1).Update(tracker);
        await downloadEngine.Received(1).RemoveTrackersAsync(1, Arg.Is<HashSet<string>>(s => s.Contains("http://tracker1.org/announce")));
        await downloadEngine.Received(1).AddTrackersAsync(1, Arg.Is<List<string>>(l => l.Contains("http://tracker2.org/announce")));
    }

    [Test]
    public async Task RenameFile_WithId_ResolvesOldPathFromFilesListAndRenames()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var file0 = new TorrentFile { Id = 100, TorrentId = 1, Path = "folder/file1.mkv" };
        var file1 = new TorrentFile { Id = 101, TorrentId = 1, Path = "folder/file2.mkv" };
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile> { file0, file1 });
        this.torrentService.RenameFileAsync(1, "folder/file2.mkv", "folder/renamed.mkv").Returns(Task.FromResult(true));

        var result = await this.controller.RenameFile("hash1", id: 1, newPath: "folder/renamed.mkv");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).RenameFileAsync(1, "folder/file2.mkv", "folder/renamed.mkv");
    }

    [Test]
    public async Task RenameFile_WithFileId_ResolvesOldPathFromFilesListAndRenames()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var file0 = new TorrentFile { Id = 100, TorrentId = 1, Path = "folder/file1.mkv" };
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile> { file0 });
        this.torrentService.RenameFileAsync(1, "folder/file1.mkv", "folder/file1_new.mkv").Returns(Task.FromResult(true));

        var result = await this.controller.RenameFile("hash1", fileId: 0, newPath: "folder/file1_new.mkv");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).RenameFileAsync(1, "folder/file1.mkv", "folder/file1_new.mkv");
    }

    [Test]
    public void GetTorrentsInfo_WithNestedSavePath_ResolvesSavePathAndContentPathCorrectly()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "Spectre 2015",
            SavePath = "/downloads/Spectre 2015",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var response = this.controller.GetTorrentsInfo();

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(1);
        list[0]["save_path"].Should().Be("/downloads");
        list[0]["content_path"].Should().Be("/downloads/Spectre 2015");
    }

    [Test]
    public void GetTorrentsInfo_WithBaseSavePath_ResolvesSavePathAndContentPathCorrectly()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "Spectre 2015",
            SavePath = "/downloads",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var response = this.controller.GetTorrentsInfo();

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(1);
        list[0]["save_path"].Should().Be("/downloads");
        list[0]["content_path"].Should().Be("/downloads/Spectre 2015");
    }

    [Test]
    public void GetTorrentsInfo_SingleFileTorrentWithExtension_ResolvesContentPathAsFileAndSavePathAsDir()
    {
        var torrent = new Torrent
        {
            Id = 2,
            InfoHash = "hash2",
            Name = "No Time to Die 2021 2160p UHD br remux dv hdr hevc-d3g",
            SavePath = "/downloads/No Time to Die 2021 2160p UHD BluRay REMUX DV HDR HEVC TrieHD 7.1 Atmos-d3g .mkv",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var response = this.controller.GetTorrentsInfo();

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(1);
        list[0]["save_path"].Should().Be("/downloads");
        list[0]["content_path"].Should().Be("/downloads/No Time to Die 2021 2160p UHD BluRay REMUX DV HDR HEVC TrieHD 7.1 Atmos-d3g .mkv");
    }

    [Test]
    public void GetTorrentsInfo_SingleFileTorrentInBaseDir_ResolvesContentPathFromTorrentFileService()
    {
        var torrent = new Torrent
        {
            Id = 3,
            InfoHash = "hash3",
            Name = "Release.Title.2021",
            SavePath = "/downloads/incomplete",
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });
        this.torrentFileService.GetFiles(3).Returns(new List<TorrentFile>
        {
            new TorrentFile { TorrentId = 3, Path = "ActualMovieFile.mkv", Size = 1000 },
        });

        var response = this.controller.GetTorrentsInfo();

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(1);
        list[0]["save_path"].Should().Be("/downloads/incomplete");
        list[0]["content_path"].Should().Be("/downloads/incomplete/ActualMovieFile.mkv");
    }

    [Test]
    public async Task TopPrio_WithBatchHashes_PreservesRelativeOrder_ByIteratingInReverse()
    {
        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", Name = "TA", QueuePosition = 3 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", Name = "TB", QueuePosition = 4 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", Name = "TC", QueuePosition = 5 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        await this.controller.TopPrio("hashA|hashB|hashC");

        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "top");
            await this.torrentService.MoveQueueAsync(20, "top");
            await this.torrentService.MoveQueueAsync(10, "top");
        });
    }

    [Test]
    public async Task BottomPrio_WithBatchHashes_PreservesRelativeOrder_ByIteratingInForward()
    {
        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", Name = "TA", QueuePosition = 1 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", Name = "TB", QueuePosition = 2 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", Name = "TC", QueuePosition = 3 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        await this.controller.BottomPrio("hashA|hashB|hashC");

        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(10, "bottom");
            await this.torrentService.MoveQueueAsync(20, "bottom");
            await this.torrentService.MoveQueueAsync(30, "bottom");
        });
    }

    [Test]
    public async Task IncreasePrio_WithBatchHashes_ProcessesInAscendingQueuePositionOrder()
    {
        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", Name = "TA", QueuePosition = 4 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", Name = "TB", QueuePosition = 2 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", Name = "TC", QueuePosition = 3 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        await this.controller.IncreasePrio("hashA|hashB|hashC");

        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(20, "up");
            await this.torrentService.MoveQueueAsync(30, "up");
            await this.torrentService.MoveQueueAsync(10, "up");
        });
    }

    [Test]
    public async Task DecreasePrio_WithBatchHashes_ProcessesInDescendingQueuePositionOrder()
    {
        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", Name = "TA", QueuePosition = 2 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", Name = "TB", QueuePosition = 3 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", Name = "TC", QueuePosition = 4 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        await this.controller.DecreasePrio("hashA|hashB|hashC");

        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "down");
            await this.torrentService.MoveQueueAsync(20, "down");
            await this.torrentService.MoveQueueAsync(10, "down");
        });
    }

    [Test]
    public void Login_WhenAuthenticationDisabled_ReturnsOkAndSetsCookie()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = this.controller.Login("any_user", "any_pass");

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Be("Ok.");
        contentResult.ContentType.Should().Be("text/plain");
        httpContext.Response.Headers.TryGetValue("Set-Cookie", out var cookies).Should().BeTrue();
        cookies.ToString().Should().Contain("SID=");
    }

    [Test]
    public void Login_WhenAuthenticationEnabled_WithCorrectPassword_ReturnsOkAndSetsCookie()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = this.controller.Login(username: "admin", password: "secret_api_key_123");

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Be("Ok.");
        httpContext.Response.Headers.TryGetValue("Set-Cookie", out var cookies).Should().BeTrue();
        cookies.ToString().Should().Contain("SID=");
    }

    [Test]
    public void Login_WhenAuthenticationEnabled_WithCorrectUsername_ReturnsOkAndSetsCookie()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = this.controller.Login(username: "secret_api_key_123", password: "wrong_password");

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Be("Ok.");
        httpContext.Response.Headers.TryGetValue("Set-Cookie", out var cookies).Should().BeTrue();
        cookies.ToString().Should().Contain("SID=");
    }

    [Test]
    public void Login_WhenAuthenticationEnabled_WithInvalidCredentials_ReturnsFails()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = this.controller.Login(username: "admin", password: "wrong_password");

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Be("Fails.");
        contentResult.ContentType.Should().Be("text/plain");
    }

    [Test]
    public void Login_WhenAuthenticationEnabled_WithUserService_ReturnsOkWhenUserValid()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("master_key");

        var userService = Substitute.For<IUserService>();
        userService.Authenticate("dbuser", "dbpass").Returns(new User { Id = 1, Username = "dbuser" });

        var customController = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            userService: userService,
            configFileProvider: this.configFileProvider);

        var httpContext = new DefaultHttpContext();
        customController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = customController.Login(username: "dbuser", password: "dbpass");

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Be("Ok.");
    }

    [Test]
    public void OnActionExecuting_WhenLoginAction_AllowsExecution()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var execContext = CreateActionExecutingContext(this.controller, httpContext, nameof(QBittorrentApiController.Login));
        this.controller.OnActionExecuting(execContext);

        execContext.Result.Should().BeNull();
    }

    [Test]
    public void OnActionExecuting_WhenUnauthenticated_Returns403Forbidden()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var execContext = CreateActionExecutingContext(this.controller, httpContext, nameof(QBittorrentApiController.GetTorrentsInfo));
        this.controller.OnActionExecuting(execContext);

        execContext.Result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)execContext.Result!;
        objResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        objResult.Value.Should().Be("Forbidden");
    }

    [Test]
    public void OnActionExecuting_WhenAuthenticatedViaApiKeyHeader_AllowsExecution()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "secret_api_key_123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var execContext = CreateActionExecutingContext(this.controller, httpContext, nameof(QBittorrentApiController.GetTorrentsInfo));
        this.controller.OnActionExecuting(execContext);

        execContext.Result.Should().BeNull();
    }

    [Test]
    public void OnActionExecuting_WhenAuthenticatedViaApiKeyQuery_AllowsExecution()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?apikey=secret_api_key_123");
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var execContext = CreateActionExecutingContext(this.controller, httpContext, nameof(QBittorrentApiController.GetTorrentsInfo));
        this.controller.OnActionExecuting(execContext);

        execContext.Result.Should().BeNull();
    }

    [Test]
    public void OnActionExecuting_WhenAuthenticatedViaSessionCookie_AllowsExecution()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");
        var loginContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = loginContext };

        this.controller.Login(username: "admin", password: "secret_api_key_123");
        loginContext.Response.Headers.TryGetValue("Set-Cookie", out var setCookieHeaders).Should().BeTrue();
        var sidCookie = setCookieHeaders.ToString().Split(';')[0].Replace("SID=", string.Empty);

        var reqContext = new DefaultHttpContext();
        reqContext.Request.Headers["Cookie"] = $"SID={sidCookie}";
        this.controller.ControllerContext = new ControllerContext { HttpContext = reqContext };

        var execContext = CreateActionExecutingContext(this.controller, reqContext, nameof(QBittorrentApiController.GetTorrentsInfo));
        this.controller.OnActionExecuting(execContext);

        execContext.Result.Should().BeNull();
    }

    [Test]
    public void GetMainData_ReturnsDynamicFreeSpaceOnDisk_FromDiskProvider()
    {
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(42949672960L);
        this.torrentService.GetAll().Returns(new List<Torrent>());

        var actionResult = this.controller.GetMainData(0);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var data = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        var serverStateJson = JsonSerializer.Serialize(data["server_state"]);
        using var doc = JsonDocument.Parse(serverStateJson);
        doc.RootElement.GetProperty("free_space_on_disk").GetInt64().Should().Be(42949672960L);
    }

    [Test]
    public void GetTorrentsInfo_CalculatesDynamicEta_WhenDownloadingWithZeroEta()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Active Download",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            TotalSize = 100_000_000,
            Downloaded = 20_000_000,
            DownloadSpeed = 10_000_000, // 80 MB remaining / 10 MB/s = 8 seconds
            Eta = 0,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = this.controller.GetTorrentsInfo();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IEnumerable<object>>().Subject.ToList();

        list.Count.Should().Be(1);
        var json = JsonSerializer.Serialize(list[0]);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("eta").GetInt64().Should().Be(8);
    }

    private static ActionExecutingContext CreateActionExecutingContext(QBittorrentApiController controller, HttpContext httpContext, string actionName)
    {
        var actionDescriptor = new ControllerActionDescriptor
        {
            ActionName = actionName,
            ControllerName = "QBittorrentApi",
            RouteValues = new Dictionary<string, string> { { "action", actionName } },
        };

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(new RouteValueDictionary { { "action", actionName } }),
            actionDescriptor);

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            controller);
    }
}
