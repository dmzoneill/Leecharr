// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    public async Task SetForceStart_WithHashesAll_SetsForceStartOnAllTorrents()
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
    public async Task AddAndRemoveTags_WithHashesAll_UpdatesTagsOnAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", Label = "oldTag" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2", Label = "oldTag" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var addResult = await this.controller.AddTags("all", "tag1, tag2");
        addResult.Should().BeOfType<ContentResult>();
        torrent1.Label.Should().Be("tag1, tag2");
        torrent2.Label.Should().Be("tag1, tag2");

        var removeResult = await this.controller.RemoveTags("all", "tag1");
        removeResult.Should().BeOfType<ContentResult>();
        torrent1.Label.Should().Be("tag2");
        torrent2.Label.Should().Be("tag2");
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
    public void SpeedLimitsMode_GetAndToggle_UpdatesStateAndEngine()
    {
        this.configService.AlternativeSpeedEnabled.Returns(false);
        this.configService.AltDownloadSpeedKbps.Returns(500);
        this.configService.AltUploadSpeedKbps.Returns(100);

        var downloadEngine = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadEngine>();
        var controllerWithEngine = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine);

        var getResult = controllerWithEngine.GetSpeedLimitsMode();
        var okGet = getResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        okGet.Value.Should().Be(0);

        var toggleResult = controllerWithEngine.ToggleSpeedLimitsMode();
        toggleResult.Should().BeOfType<ContentResult>();

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["AlternativeSpeedEnabled"] == true));
    }
}
