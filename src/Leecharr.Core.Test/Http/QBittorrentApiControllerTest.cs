// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.QBittorrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NLog;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers.Search;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Tags;
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
    private IDownloadEngine downloadEngine = null!;
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
        this.downloadEngine = Substitute.For<IDownloadEngine>();

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
            downloadEngine: this.downloadEngine,
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

        this.torrentFileService.Received(1).GetFilesForTorrents(Arg.Any<IEnumerable<int>>());
        this.torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
    }

    [Test]
    public void GetTorrentsInfo_BatchLoadsFiles_DoesNotQueryPerTorrent()
    {
        var torrent1 = new Torrent { Id = 1, Name = "Torrent 1", InfoHash = "hash1", Status = TorrentStatus.Downloading };
        var torrent2 = new Torrent { Id = 2, Name = "Torrent 2", InfoHash = "hash2", Status = TorrentStatus.Seeding };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var actionResult = this.controller.GetTorrentsInfo();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(2);

        this.torrentFileService.Received(1).GetFilesForTorrents(Arg.Any<IEnumerable<int>>());
        this.torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
    }

    [Test]
    public void GetMainData_IncludesSequentialDownloadAndFirstLastPiecePriority()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            SequentialDownload = true,
            FirstLastPiecePriority = true,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = this.controller.GetMainData(0);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var data = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        var torrents = data["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        var torrentObj = torrents["hash1"];
        torrentObj.Should().NotBeNull();

        var json = JsonSerializer.Serialize(torrentObj);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("seq_dl").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("f_l_piece_prio").GetBoolean().Should().BeTrue();
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
    public void GetMainData_WithSubsequentRid_WhenQueueIsEmpty_ReturnsIncrementalUpdateWithFullUpdateFalse()
    {
        this.torrentService.GetAll().Returns(new List<Torrent>());

        // Initial sync on empty queue
        var initial = this.controller.GetMainData(0);
        var initialData = ((OkObjectResult)initial.Result!).Value as Dictionary<string, object>;
        initialData!["full_update"].Should().Be(true);
        var initialRid = (int)initialData["rid"];
        initialRid.Should().Be(1);

        // Subsequent syncs on empty queue must return incremental delta (full_update: false)
        var delta1 = this.controller.GetMainData(initialRid);
        var deltaData1 = ((OkObjectResult)delta1.Result!).Value as Dictionary<string, object>;
        deltaData1!["full_update"].Should().Be(false);
        deltaData1["rid"].Should().Be(2);

        var delta2 = this.controller.GetMainData(2);
        var deltaData2 = ((OkObjectResult)delta2.Result!).Value as Dictionary<string, object>;
        deltaData2!["full_update"].Should().Be(false);
        deltaData2["rid"].Should().Be(3);
    }

    [Test]
    public void GetMainData_WithOlderRid_ReturnsTorrentsChangedSinceRequestedRid()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.1,
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Name = "Torrent 2",
            InfoHash = "hash2",
            Status = TorrentStatus.Downloading,
            Progress = 0.1,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        // Initial full sync (RID = 1)
        var initial = this.controller.GetMainData(0);
        var initialData = ((OkObjectResult)initial.Result!).Value as Dictionary<string, object>;
        initialData!["full_update"].Should().Be(true);
        initialData["rid"].Should().Be(1);

        // Update torrent 1 at RID = 2
        var modified1 = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { modified1, torrent2 });
        var poll1 = this.controller.GetMainData(1);
        var poll1Data = ((OkObjectResult)poll1.Result!).Value as Dictionary<string, object>;
        poll1Data!["full_update"].Should().Be(false);
        poll1Data["rid"].Should().Be(2);

        // Update torrent 2 at RID = 3 (torrent 1 unchanged)
        var modified2 = new Torrent
        {
            Id = 2,
            Name = "Torrent 2",
            InfoHash = "hash2",
            Status = TorrentStatus.Downloading,
            Progress = 0.8,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { modified1, modified2 });
        var poll2 = this.controller.GetMainData(2);
        var poll2Data = ((OkObjectResult)poll2.Result!).Value as Dictionary<string, object>;
        poll2Data!["full_update"].Should().Be(false);
        poll2Data["rid"].Should().Be(3);

        // Now poll with older rid = 1 (server is at CurrentRid = 3)
        // Should return both modified1 (changed at RID 2 > 1) and modified2 (changed at RID 3 > 1)
        var olderPoll = this.controller.GetMainData(1);
        var olderPollData = ((OkObjectResult)olderPoll.Result!).Value as Dictionary<string, object>;
        olderPollData!["full_update"].Should().Be(false);
        olderPollData["rid"].Should().Be(4);
        var olderTorrents = olderPollData["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        olderTorrents.Count.Should().Be(2);
        olderTorrents.Contains("hash1").Should().BeTrue();
        olderTorrents.Contains("hash2").Should().BeTrue();

        // Now poll with rid = 2 (server is at CurrentRid = 4)
        // modified1 was changed at RID 2 (not > 2), modified2 was changed at RID 3 (> 2)
        var pollFromRid2 = this.controller.GetMainData(2);
        var pollFromRid2Data = ((OkObjectResult)pollFromRid2.Result!).Value as Dictionary<string, object>;
        pollFromRid2Data!["full_update"].Should().Be(false);
        var torrentsFromRid2 = pollFromRid2Data["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        torrentsFromRid2.Count.Should().Be(1);
        torrentsFromRid2.Contains("hash2").Should().BeTrue();
        torrentsFromRid2.Contains("hash1").Should().BeFalse();
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
    public async Task SetSavePath_WithHashesAndLocation_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent { Id = 10, Name = "Torrent 1", InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = await this.controller.SetSavePath(hashes: "hash1", location: "/downloads/savepath");

        result.Should().BeOfType<ContentResult>();
        ((ContentResult)result).Content.Should().Be("Ok.");
        await this.torrentService.Received(1).SetLocationAsync(10, "/downloads/savepath", moveFiles: true);
    }

    [Test]
    public async Task SetSavePath_WithPathAndId_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent { Id = 10, Name = "Torrent 1", InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = await this.controller.SetSavePath(id: "hash1", path: "/downloads/savepath");

        result.Should().BeOfType<ContentResult>();
        ((ContentResult)result).Content.Should().Be("Ok.");
        await this.torrentService.Received(1).SetLocationAsync(10, "/downloads/savepath", moveFiles: true);
    }

    [Test]
    public async Task SetSavePath_WithNumericIdAndPath_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent { Id = 42, Name = "Torrent 42", InfoHash = "hash42" };
        this.torrentService.Get(42).Returns(torrent);

        var result = await this.controller.SetSavePath(id: "42", path: "/downloads/savepath");

        result.Should().BeOfType<ContentResult>();
        ((ContentResult)result).Content.Should().Be("Ok.");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/savepath", moveFiles: true);
    }

    [Test]
    public async Task SetSavePath_WithMultipleIds_InvokesSetLocationAsyncForEachTorrent()
    {
        var torrent1 = new Torrent { Id = 10, Name = "Torrent 1", InfoHash = "hash1" };
        var torrent2 = new Torrent { Id = 20, Name = "Torrent 2", InfoHash = "hash2" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);
        this.torrentService.GetByInfoHash("hash2").Returns(torrent2);

        var result = await this.controller.SetSavePath(id: "hash1|hash2", path: "/downloads/savepath");

        result.Should().BeOfType<ContentResult>();
        ((ContentResult)result).Content.Should().Be("Ok.");
        await this.torrentService.Received(1).SetLocationAsync(10, "/downloads/savepath", moveFiles: true);
        await this.torrentService.Received(1).SetLocationAsync(20, "/downloads/savepath", moveFiles: true);
    }

    [Test]
    public async Task SetDownloadPath_WithHashesAndPath_InvokesSetLocationAsyncWithMoveFalse()
    {
        var torrent = new Torrent { Id = 10, Name = "Torrent 1", InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = await this.controller.SetDownloadPath(hashes: "hash1", path: "/downloads/incomplete");

        result.Should().BeOfType<ContentResult>();
        ((ContentResult)result).Content.Should().Be("Ok.");
        await this.torrentService.Received(1).SetLocationAsync(10, "/downloads/incomplete", moveFiles: false);
    }

    [Test]
    public async Task SetDownloadPath_WithIdAndPath_InvokesSetLocationAsyncWithMoveFalse()
    {
        var torrent = new Torrent { Id = 10, Name = "Torrent 1", InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = await this.controller.SetDownloadPath(id: "hash1", path: "/downloads/incomplete");

        result.Should().BeOfType<ContentResult>();
        ((ContentResult)result).Content.Should().Be("Ok.");
        await this.torrentService.Received(1).SetLocationAsync(10, "/downloads/incomplete", moveFiles: false);
    }

    [Test]
    public async Task SetDownloadPath_WithEmptyOrMissingParams_ReturnsOkAndDoesNotInvokeSetLocationAsync()
    {
        var result = await this.controller.SetDownloadPath(hashes: null, id: null, path: null);

        result.Should().BeOfType<ContentResult>();
        ((ContentResult)result).Content.Should().Be("Ok.");
        await this.torrentService.DidNotReceiveWithAnyArgs().SetLocationAsync(default, default, default);
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
    public void GetTorrentsInfo_IncludesSequentialDownloadAndFirstLastPiecePriority()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "T1",
            SequentialDownload = true,
            FirstLastPiecePriority = true,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var response = this.controller.GetTorrentsInfo();

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject.ToList();
        list.Should().HaveCount(1);
        list[0]["seq_dl"].Should().Be(true);
        list[0]["f_l_piece_prio"].Should().Be(true);
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
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var result = await this.controller.SetForceStart("hash1", "false");

        result.Should().BeOfType<ContentResult>();
        torrent1.ForceStart.Should().BeFalse();
        await this.torrentService.Received(1).UpdateAsync(torrent1);
        await this.torrentService.DidNotReceive().ResumeAsync(Arg.Any<int>());
    }

    [Test]
    public async Task SetSuperSeeding_WithHashesAll_SetsSuperSeedingOnAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", Progress = 1.0, Status = TorrentStatus.Seeding };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2", Progress = 1.0, Status = TorrentStatus.Seeding };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.SetSuperSeeding("all", true);

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).SetSuperSeedingAsync(1, true);
        await this.torrentService.Received(1).SetSuperSeedingAsync(2, true);
    }

    [Test]
    public async Task SetSuperSeeding_WhenIncompleteOrNotSeeding_ReturnsBadRequest()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", Progress = 0.5, Status = TorrentStatus.Downloading };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var result = await this.controller.SetSuperSeeding("hash1", true);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Super seeding can only be enabled for 100% completed seeding torrents.");
        await this.torrentService.DidNotReceive().SetSuperSeedingAsync(Arg.Any<int>(), Arg.Any<bool>());
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
    public async Task AddAndRemoveTags_WithTagRepository_SynchronizesTagIdsAndLabel()
    {
        var tagRepo = Substitute.For<ITagRepository>();
        var tags = new List<Tag>
        {
            new() { Id = 1, Label = "oldTag" },
            new() { Id = 2, Label = "tag1" },
            new() { Id = 3, Label = "tag2" },
        };
        tagRepo.All().Returns(_ => tags);

        var controllerWithTags = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            configFileProvider: this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService,
            downloadEngine: this.downloadEngine,
            diskProvider: this.diskProvider,
            tagRepository: tagRepo);

        var torrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", Label = "oldTag", TagIds = new List<int> { 1 } };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var addResult = await controllerWithTags.AddTags("all", "tag1, tag2");
        addResult.Should().BeOfType<ContentResult>();
        torrent.Label.Should().Be("oldTag, tag1, tag2");
        torrent.TagIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });

        var removeResult = await controllerWithTags.RemoveTags("all", "tag1");
        removeResult.Should().BeOfType<ContentResult>();
        torrent.Label.Should().Be("oldTag, tag2");
        torrent.TagIds.Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Test]
    public async Task DeleteTags_WithTagRepository_RemovesFromTorrentsAndDeletesFromRepo()
    {
        var tagRepo = Substitute.For<ITagRepository>();
        var tags = new List<Tag>
        {
            new() { Id = 1, Label = "tag1" },
            new() { Id = 2, Label = "tag2" },
        };
        tagRepo.All().Returns(_ => tags);

        var controllerWithTags = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            configFileProvider: this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService,
            downloadEngine: this.downloadEngine,
            diskProvider: this.diskProvider,
            tagRepository: tagRepo);

        var torrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", Label = "tag1, tag2", TagIds = new List<int> { 1, 2 } };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var deleteResult = await controllerWithTags.DeleteTags("tag1");
        deleteResult.Should().BeOfType<ContentResult>();
        torrent.Label.Should().Be("tag2");
        torrent.TagIds.Should().BeEquivalentTo(new[] { 2 });
        tagRepo.Received(1).Delete(1);
    }

    [Test]
    public async Task AddTorrents_WithTagsAndTagRepository_SynchronizesTagIdsAndLabel()
    {
        var tagRepo = Substitute.For<ITagRepository>();
        var tags = new List<Tag>
        {
            new() { Id = 1, Label = "movie" },
            new() { Id = 2, Label = "4k" },
        };
        tagRepo.All().Returns(_ => tags);

        var controllerWithTags = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            configFileProvider: this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService,
            downloadEngine: this.downloadEngine,
            diskProvider: this.diskProvider,
            tagRepository: tagRepo);

        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(addedTorrent);

        var result = await controllerWithTags.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash1",
            Tags = "movie, 4k",
        });

        result.Should().BeOfType<ContentResult>();
        addedTorrent.Label.Should().Be("movie, 4k");
        addedTorrent.TagIds.Should().BeEquivalentTo(new[] { 1, 2 });
        await this.torrentService.Received(1).UpdateAsync(addedTorrent);
    }

    [Test]
    public async Task AddTorrents_WithSequentialDownloadTrue_SetsSequentialDownload()
    {
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash1",
            SequentialDownload = "true",
            FirstLastPiecePrio = "false",
        });

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

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash1",
            SequentialDownload = "false",
            FirstLastPiecePrio = "true",
        });

        result.Should().BeOfType<ContentResult>();
        addedTorrent.SequentialDownload.Should().BeFalse();
    }

    [Test]
    public async Task AddTorrents_WithDownloadPath_UsesDownloadPathAsSavepathForMagnet()
    {
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", "movies", "/custom/download/path", false)
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash1",
            Category = "movies",
            DownloadPath = "/custom/download/path",
        });

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", "movies", "/custom/download/path", false);
    }

    [Test]
    public async Task AddTorrents_WithDownloadUnderscorePath_UsesDownloadPathAsSavepathForMagnet()
    {
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", "movies", "/custom/download_path", false)
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash1",
            Category = "movies",
            Download_path = "/custom/download_path",
        });

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", "movies", "/custom/download_path", false);
    }

    [Test]
    public async Task AddTorrents_WithSavepathAndDownloadPath_PrefersSavepath()
    {
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", "movies", "/priority/savepath", false)
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash1",
            Category = "movies",
            Savepath = "/priority/savepath",
            DownloadPath = "/ignored/download/path",
        });

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync("magnet:?xt=urn:btih:hash1", "movies", "/priority/savepath", false);
    }

    [Test]
    public async Task AddTorrents_WithHttpUrlAndCookie_PassesCookieHeaderToSafeHttpClientService()
    {
        var dummyBytes = new byte[] { 1, 2, 3 };
        var parsed = new ParsedTorrent { InfoHash = "hash1", Name = "T1" };
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };

        this.safeHttpClientService.DownloadBytesAsync(
            Arg.Is<Uri>(u => u.ToString() == "https://tracker.example.com/torrent.torrent"),
            Arg.Any<long>(),
            Arg.Is<IDictionary<string, string>>(h => h != null && h.ContainsKey("Cookie") && h["Cookie"] == "uid=123; pass=secret"),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(dummyBytes);

        this.torrentFileParser.Parse(dummyBytes).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, "tv", "/downloads/tv", false, dummyBytes)
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "https://tracker.example.com/torrent.torrent",
            Category = "tv",
            DownloadPath = "/downloads/tv",
            Cookie = "uid=123; pass=secret",
        });

        result.Should().BeOfType<ContentResult>();
        await this.safeHttpClientService.Received(1).DownloadBytesAsync(
            Arg.Is<Uri>(u => u.ToString() == "https://tracker.example.com/torrent.torrent"),
            Arg.Any<long>(),
            Arg.Is<IDictionary<string, string>>(h => h["Cookie"] == "uid=123; pass=secret"),
            Arg.Any<System.Threading.CancellationToken>());
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, "tv", "/downloads/tv", false, dummyBytes);
    }

    [Test]
    public async Task AddTorrents_WithHttpUrlAndCookiesPlural_PassesCookieHeaderToSafeHttpClientService()
    {
        var dummyBytes = new byte[] { 1, 2, 3 };
        var parsed = new ParsedTorrent { InfoHash = "hash1", Name = "T1" };
        var addedTorrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };

        this.safeHttpClientService.DownloadBytesAsync(
            Arg.Is<Uri>(u => u.ToString() == "https://tracker.example.com/torrent.torrent"),
            Arg.Any<long>(),
            Arg.Is<IDictionary<string, string>>(h => h != null && h.ContainsKey("Cookie") && h["Cookie"] == "auth=token123"),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(dummyBytes);

        this.torrentFileParser.Parse(dummyBytes).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, dummyBytes)
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "https://tracker.example.com/torrent.torrent",
            Cookies = "auth=token123",
        });

        result.Should().BeOfType<ContentResult>();
        await this.safeHttpClientService.Received(1).DownloadBytesAsync(
            Arg.Is<Uri>(u => u.ToString() == "https://tracker.example.com/torrent.torrent"),
            Arg.Any<long>(),
            Arg.Is<IDictionary<string, string>>(h => h["Cookie"] == "auth=token123"),
            Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public async Task AddTorrents_WithUploadedFileAndDownloadPath_UsesDownloadPathAsSavepath()
    {
        var dummyBytes = new byte[] { 4, 5, 6 };
        var parsed = new ParsedTorrent { InfoHash = "hash2", Name = "T2" };
        var addedTorrent = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };

        var formFile = Substitute.For<IFormFile>();
        formFile.Length.Returns(dummyBytes.Length);
        formFile.CopyToAsync(Arg.Any<Stream>()).Returns(ci =>
        {
            var stream = ci.Arg<Stream>();
            stream.Write(dummyBytes, 0, dummyBytes.Length);
            return Task.CompletedTask;
        });

        this.torrentFileParser.Parse(Arg.Is<byte[]>(b => b.SequenceEqual(dummyBytes))).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, "movies", "/custom/download/path", false, Arg.Any<byte[]>())
            .Returns(addedTorrent);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Torrents = new List<IFormFile> { formFile },
            Category = "movies",
            DownloadPath = "/custom/download/path",
        });

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, "movies", "/custom/download/path", false, Arg.Any<byte[]>());
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

        await this.controller.SetShareLimits("all", ratioLimit: 2.0, seedingTimeLimit: 120, maxRatioAction: 1);
        torrent1.TargetRatio.Should().Be(2.0);
        torrent1.TargetSeedTimeMinutes.Should().Be(120);
        torrent1.ShareLimitAction.Should().Be("Remove");
    }

    [Test]
    public void GetTorrentList_ReturnsSeedingTimeLimitInMinutes()
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
        list[0]["seeding_time_limit"].Should().Be(60);
        list[0]["max_seeding_time"].Should().Be(60);
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

        var result = controllerWithEngine.GetTorrentPeers("hash1", rid: 0);
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var data = okResult.Value;

        var fullUpdate = (bool)data!.GetType().GetProperty("full_update")!.GetValue(data)!;
        var rid = (int)data!.GetType().GetProperty("rid")!.GetValue(data)!;
        var peerDict = (Dictionary<string, object>)data!.GetType().GetProperty("peers")!.GetValue(data)!;

        fullUpdate.Should().BeTrue();
        rid.Should().Be(1);
        peerDict.Should().ContainKey("192.168.1.50:6881");
    }

    [Test]
    public void GetTorrentPeers_WithRidGreaterThanZero_ReturnsIncrementalUpdateWithRemovedPeers()
    {
        var downloadEngine = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadEngine>();
        var downloadTask = Substitute.For<NzbDrone.Core.BitTorrent.IDownloadTask>();
        var peer1 = new NzbDrone.Core.BitTorrent.PeerInfo
        {
            Ip = "192.168.1.50",
            Port = 6881,
            Client = "Leecharr/1.0",
            Flags = "uI",
            Progress = 0.5,
            DownloadSpeed = 1048576,
            UploadSpeed = 524288,
            Downloaded = 100000000,
            Uploaded = 50000000,
        };
        var peer2 = new NzbDrone.Core.BitTorrent.PeerInfo
        {
            Ip = "192.168.1.51",
            Port = 6882,
            Client = "qBittorrent/4.5.0",
            Flags = "d",
            Progress = 0.2,
            DownloadSpeed = 512000,
            UploadSpeed = 0,
            Downloaded = 20000000,
            Uploaded = 0,
        };

        downloadTask.GetPeers().Returns(new List<NzbDrone.Core.BitTorrent.PeerInfo> { peer1, peer2 });
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

        // Initial sync: rid = 0 returns full update
        var initialResult = controllerWithEngine.GetTorrentPeers("hash1", rid: 0);
        var initialData = initialResult.Should().BeOfType<OkObjectResult>().Subject.Value;
        var initialFullUpdate = (bool)initialData!.GetType().GetProperty("full_update")!.GetValue(initialData)!;
        var initialRid = (int)initialData!.GetType().GetProperty("rid")!.GetValue(initialData)!;

        initialFullUpdate.Should().BeTrue();
        initialRid.Should().Be(1);

        // Incremental sync: peer2 disconnects, peer1 updates download speed, peer3 connects
        var peer1Updated = new NzbDrone.Core.BitTorrent.PeerInfo
        {
            Ip = "192.168.1.50",
            Port = 6881,
            Client = "Leecharr/1.0",
            Flags = "uI",
            Progress = 0.6,
            DownloadSpeed = 2097152,
            UploadSpeed = 524288,
            Downloaded = 120000000,
            Uploaded = 50000000,
        };
        var peer3 = new NzbDrone.Core.BitTorrent.PeerInfo
        {
            Ip = "192.168.1.52",
            Port = 6883,
            Client = "Transmission/3.0",
            Flags = "u",
            Progress = 0.1,
            DownloadSpeed = 100000,
            UploadSpeed = 10000,
            Downloaded = 1000000,
            Uploaded = 500000,
        };

        downloadTask.GetPeers().Returns(new List<NzbDrone.Core.BitTorrent.PeerInfo> { peer1Updated, peer3 });

        var deltaResult = controllerWithEngine.GetTorrentPeers("hash1", rid: 1);
        var deltaData = deltaResult.Should().BeOfType<OkObjectResult>().Subject.Value;
        var deltaFullUpdate = (bool)deltaData!.GetType().GetProperty("full_update")!.GetValue(deltaData)!;
        var deltaRid = (int)deltaData!.GetType().GetProperty("rid")!.GetValue(deltaData)!;
        var deltaPeers = (Dictionary<string, object>)deltaData!.GetType().GetProperty("peers")!.GetValue(deltaData)!;
        var peersRemoved = (string[])deltaData!.GetType().GetProperty("peers_removed")!.GetValue(deltaData)!;

        deltaFullUpdate.Should().BeFalse();
        deltaRid.Should().Be(2);
        deltaPeers.Should().ContainKey("192.168.1.50:6881");
        deltaPeers.Should().ContainKey("192.168.1.52:6883");
        deltaPeers.Should().NotContainKey("192.168.1.51:6882");
        peersRemoved.Should().ContainSingle().Which.Should().Be("192.168.1.51:6882");
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
        var createdDate = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var addedDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "T1",
            CreatedBy = "Leecharr",
            CreationDate = createdDate,
            DateAdded = addedDate,
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
        this.downloadEngine.GetTorrentResourceMetrics(1).Returns(new TorrentResourceMetrics
        {
            WastedBytes = 8192,
        });

        var actionResult = this.controller.GetProperties("hash1");
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dict = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        dict.Should().ContainKey("creation_date");
        dict.Should().ContainKey("addition_date");
        dict.Should().ContainKey("completion_date");
        dict.Should().ContainKey("created_by");
        dict.Should().ContainKey("dl_speed");
        dict.Should().ContainKey("up_speed");
        dict.Should().ContainKey("eta");
        dict.Should().ContainKey("peers");
        dict.Should().ContainKey("seeds");
        dict.Should().ContainKey("total_size");
        dict.Should().ContainKey("total_wasted");
        dict.Should().ContainKey("piece_size");

        dict["creation_date"].Should().Be(new DateTimeOffset(createdDate).ToUnixTimeSeconds());
        dict["addition_date"].Should().Be(new DateTimeOffset(addedDate).ToUnixTimeSeconds());
        dict["total_wasted"].Should().Be(8192L);
        dict["piece_size"].Should().Be(262144);
        dict["dl_speed"].Should().Be(1048576L);
        dict["up_speed"].Should().Be(524288L);
        dict["eta"].Should().Be(300L);
        dict["seeds"].Should().Be(5);
        dict["peers"].Should().Be(10);
        dict["total_size"].Should().Be(1000000000L);
    }

    [Test]
    public void GetProperties_CalculatesFallbackPieceSizeAndCreationDateWhenMissing()
    {
        var addedDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrent = new Torrent
        {
            Id = 2,
            InfoHash = "hash2",
            Name = "T2",
            CreationDate = null,
            DateAdded = addedDate,
            TotalSize = 10485760,
            PieceLength = 0,
            PieceCount = 40,
        };
        this.torrentService.GetByInfoHash("hash2").Returns(torrent);
        this.downloadEngine.GetTorrentResourceMetrics(2).Returns((TorrentResourceMetrics)null!);

        var actionResult = this.controller.GetProperties("hash2");
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dict = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        dict["creation_date"].Should().Be(new DateTimeOffset(addedDate).ToUnixTimeSeconds());
        dict["piece_size"].Should().Be((int)(10485760 / 40));
        dict["total_wasted"].Should().Be(0L);
    }

    [Test]
    public async Task SetPreferences_UpdatesConfigService()
    {
        var json = "{\"dl_limit\":10485760,\"up_limit\":5242880,\"dht\":true,\"pex\":true,\"save_path\":\"/data/downloads\"}";
        var result = await this.controller.SetPreferencesAsync(json);
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
    public void GetTorrentsInfo_WithCategorySubPath_ReportsSavePathAsCompletedDir()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "Spectre 2015",
            Category = "radarr",
            SavePath = "/downloads/radarr",
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
        var files = new List<TorrentFile>
        {
            new TorrentFile { TorrentId = 3, Path = "ActualMovieFile.mkv", Size = 1000 },
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });
        this.torrentFileService.GetFiles(3).Returns(files);
        this.torrentFileService.GetFilesForTorrents(Arg.Any<IEnumerable<int>>())
            .Returns(new Dictionary<int, List<TorrentFile>> { [3] = files });

        var response = this.controller.GetTorrentsInfo();

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(1);
        list[0]["save_path"].Should().Be("/downloads");
        list[0]["content_path"].Should().Be("/downloads/ActualMovieFile.mkv");
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
    public void Login_WhenHttps_SetsSecureSidCookie()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = this.controller.Login("any_user", "any_pass");

        result.Should().BeOfType<ContentResult>();
        httpContext.Response.Headers.TryGetValue("Set-Cookie", out var cookies).Should().BeTrue();
        cookies.ToString().Should().Contain("SID=");
        cookies.ToString().ToLowerInvariant().Should().Contain("secure");
    }

    [Test]
    public void Login_WhenHttp_DoesNotSetSecureSidCookie()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = this.controller.Login("any_user", "any_pass");

        result.Should().BeOfType<ContentResult>();
        httpContext.Response.Headers.TryGetValue("Set-Cookie", out var cookies).Should().BeTrue();
        cookies.ToString().Should().Contain("SID=");
        cookies.ToString().ToLowerInvariant().Should().NotContain("secure");
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

    [Test]
    public void GetTorrentsInfo_WithTag_FiltersMatchingTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", Label = "movies, 4k" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2", Label = "tv, 1080p" };
        var torrent3 = new Torrent { Id = 3, InfoHash = "hash3", Name = "T3", Label = null };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2, torrent3 });

        var response = this.controller.GetTorrentsInfo(tag: "4k");
        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list.Should().HaveCount(1);
        list[0]["hash"].Should().Be("hash1");

        var responseCaseInsensitive = this.controller.GetTorrentsInfo(tag: "  TV  ");
        var listCaseInsensitive = responseCaseInsensitive.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listCaseInsensitive.Should().HaveCount(1);
        listCaseInsensitive[0]["hash"].Should().Be("hash2");

        var responseNone = this.controller.GetTorrentsInfo(tag: "nonexistent");
        var listNone = responseNone.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listNone.Should().BeEmpty();
    }

    [Test]
    public void GetTorrentsInfo_WithFilterStalled_FiltersStalledAndZeroSpeedTransfers()
    {
        var t1 = new Torrent { Id = 1, InfoHash = "h1", Status = TorrentStatus.Stalled };
        var t2 = new Torrent { Id = 2, InfoHash = "h2", Status = TorrentStatus.Downloading, DownloadSpeed = 0 };
        var t3 = new Torrent { Id = 3, InfoHash = "h3", Status = TorrentStatus.Seeding, UploadSpeed = 0 };
        var t4 = new Torrent { Id = 4, InfoHash = "h4", Status = TorrentStatus.Downloading, DownloadSpeed = 500 };
        var t5 = new Torrent { Id = 5, InfoHash = "h5", Status = TorrentStatus.Seeding, UploadSpeed = 500 };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3, t4, t5 });

        var response = this.controller.GetTorrentsInfo(filter: "stalled");
        var list = response.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        list.Should().HaveCount(3);
        list.Select(d => d["hash"]).Should().BeEquivalentTo(new[] { "h1", "h2", "h3" });
    }

    [Test]
    public void GetTorrentsInfo_WithFilterStalledDownloading_FiltersCorrectly()
    {
        var t1 = new Torrent { Id = 1, InfoHash = "h1", Status = TorrentStatus.Stalled, Progress = 0.5 };
        var t2 = new Torrent { Id = 2, InfoHash = "h2", Status = TorrentStatus.Downloading, DownloadSpeed = 0, Progress = 0.5 };
        var t3 = new Torrent { Id = 3, InfoHash = "h3", Status = TorrentStatus.Stalled, Progress = 1.0 };
        var t4 = new Torrent { Id = 4, InfoHash = "h4", Status = TorrentStatus.Seeding, UploadSpeed = 0, Progress = 1.0 };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3, t4 });

        var response = this.controller.GetTorrentsInfo(filter: "stalled_downloading");
        var list = response.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        list.Should().HaveCount(2);
        list.Select(d => d["hash"]).Should().BeEquivalentTo(new[] { "h1", "h2" });
    }

    [Test]
    public void GetTorrentsInfo_WithFilterStalledUploading_FiltersCorrectly()
    {
        var t1 = new Torrent { Id = 1, InfoHash = "h1", Status = TorrentStatus.Stalled, Progress = 1.0 };
        var t2 = new Torrent { Id = 2, InfoHash = "h2", Status = TorrentStatus.Seeding, UploadSpeed = 0, Progress = 1.0 };
        var t3 = new Torrent { Id = 3, InfoHash = "h3", Status = TorrentStatus.Stalled, Progress = 0.5 };
        var t4 = new Torrent { Id = 4, InfoHash = "h4", Status = TorrentStatus.Downloading, DownloadSpeed = 0, Progress = 0.5 };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3, t4 });

        var response = this.controller.GetTorrentsInfo(filter: "stalled_uploading");
        var list = response.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        list.Should().HaveCount(2);
        list.Select(d => d["hash"]).Should().BeEquivalentTo(new[] { "h1", "h2" });
    }

    [Test]
    public void GetTorrentsInfo_WithFilterChecking_FiltersCorrectly()
    {
        var t1 = new Torrent { Id = 1, InfoHash = "h1", Status = TorrentStatus.Checking };
        var t2 = new Torrent { Id = 2, InfoHash = "h2", Status = TorrentStatus.Downloading };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2 });

        var response = this.controller.GetTorrentsInfo(filter: "checking");
        var list = response.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        list.Should().HaveCount(1);
        list[0]["hash"].Should().Be("h1");
    }

    [Test]
    public void GetTorrentsInfo_WithFilterErrored_FiltersCorrectly()
    {
        var t1 = new Torrent { Id = 1, InfoHash = "h1", Status = TorrentStatus.Error };
        var t2 = new Torrent { Id = 2, InfoHash = "h2", Status = TorrentStatus.Downloading };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2 });

        var response = this.controller.GetTorrentsInfo(filter: "errored");
        var list = response.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        list.Should().HaveCount(1);
        list[0]["hash"].Should().Be("h1");

        var responseError = this.controller.GetTorrentsInfo(filter: "error");
        var listError = responseError.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        listError.Should().HaveCount(1);
        listError[0]["hash"].Should().Be("h1");
    }

    [Test]
    public void GetTorrentsInfo_WithFilterResumed_FiltersCorrectly()
    {
        var t1 = new Torrent { Id = 1, InfoHash = "h1", Status = TorrentStatus.Downloading };
        var t2 = new Torrent { Id = 2, InfoHash = "h2", Status = TorrentStatus.Seeding };
        var t3 = new Torrent { Id = 3, InfoHash = "h3", Status = TorrentStatus.Paused };
        var t4 = new Torrent { Id = 4, InfoHash = "h4", Status = TorrentStatus.Stopped };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3, t4 });

        var response = this.controller.GetTorrentsInfo(filter: "resumed");
        var list = response.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        list.Should().HaveCount(2);
        list.Select(d => d["hash"]).Should().BeEquivalentTo(new[] { "h1", "h2" });

        var responseRunning = this.controller.GetTorrentsInfo(filter: "running");
        var listRunning = responseRunning.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;

        listRunning.Should().HaveCount(2);
        listRunning.Select(d => d["hash"]).Should().BeEquivalentTo(new[] { "h1", "h2" });
    }

    [Test]
    public void GetMainData_FullUpdate_IncludesCategoriesWithSavePath()
    {
        var category1 = new Category { Id = 1, Name = "tv", SavePath = "/downloads/tv" };
        var category2 = new Category { Id = 2, Name = "movies", SavePath = "/downloads/movies" };
        var category3 = new Category { Id = 3, Name = "default_cat", SavePath = null! };

        this.categoryService.GetAll().Returns(new List<Category> { category1, category2, category3 });
        this.torrentService.GetAll().Returns(new List<Torrent>());

        var actionResult = this.controller.GetMainData(0);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var data = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;

        data["full_update"].Should().Be(true);
        var json = JsonSerializer.Serialize(data["categories"]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("tv", out var tvCat).Should().BeTrue();
        tvCat.GetProperty("name").GetString().Should().Be("tv");
        tvCat.GetProperty("savePath").GetString().Should().Be("/downloads/tv");

        root.TryGetProperty("movies", out var movieCat).Should().BeTrue();
        movieCat.GetProperty("name").GetString().Should().Be("movies");
        movieCat.GetProperty("savePath").GetString().Should().Be("/downloads/movies");

        root.TryGetProperty("default_cat", out var defCat).Should().BeTrue();
        defCat.GetProperty("name").GetString().Should().Be("default_cat");
        defCat.GetProperty("savePath").GetString().Should().Be(string.Empty);
    }

    [Test]
    public void GetMainData_DeltaUpdate_IncludesCategoriesWithSavePath()
    {
        var category = new Category { Id = 1, Name = "music", SavePath = "/downloads/music" };
        this.categoryService.GetAll().Returns(new List<Category> { category });
        this.torrentService.GetAll().Returns(new List<Torrent>());

        // Initial full sync
        var initial = this.controller.GetMainData(0);
        var initialData = ((OkObjectResult)initial.Result!).Value as Dictionary<string, object>;
        var initialRid = (int)initialData!["rid"];

        // Subsequent delta sync
        var delta = this.controller.GetMainData(initialRid);
        var deltaData = ((OkObjectResult)delta.Result!).Value as Dictionary<string, object>;
        deltaData!["full_update"].Should().Be(false);

        var json = JsonSerializer.Serialize(deltaData["categories"]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("music", out var musicCat).Should().BeTrue();
        musicCat.GetProperty("name").GetString().Should().Be("music");
        musicCat.GetProperty("savePath").GetString().Should().Be("/downloads/music");
    }

    [Test]
    public void GetCategories_ReturnsMappedCategoriesWithSavePath()
    {
        var category1 = new Category { Id = 1, Name = "anime", SavePath = "/downloads/anime" };
        var category2 = new Category { Id = 2, Name = "books", SavePath = null! };

        this.categoryService.GetAll().Returns(new List<Category> { category1, category2 });

        var actionResult = this.controller.GetCategories();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("anime", out var animeCat).Should().BeTrue();
        animeCat.GetProperty("name").GetString().Should().Be("anime");
        animeCat.GetProperty("savePath").GetString().Should().Be("/downloads/anime");

        root.TryGetProperty("books", out var booksCat).Should().BeTrue();
        booksCat.GetProperty("name").GetString().Should().Be("books");
        booksCat.GetProperty("savePath").GetString().Should().Be(string.Empty);
    }

    [Test]
    public async Task ReannounceTorrents_WithHashesAll_ReannouncesAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = await this.controller.ReannounceTorrents("all");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).ForceAnnounceAsync(1);
        await this.torrentService.Received(1).ForceAnnounceAsync(2);
    }

    [Test]
    public async Task ReannounceTorrents_WithSpecificHashes_ReannouncesSelectedTorrents()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var result = await this.controller.ReannounceTorrents("hash1");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).ForceAnnounceAsync(1);
    }

    [Test]
    public async Task ReannounceTorrents_WithEmptyHashes_ReturnsBadRequest()
    {
        var result = await this.controller.ReannounceTorrents(string.Empty);

        result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public async Task ToggleSequentialDownload_TogglesSequentialDownloadAndCallsEngine()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", SequentialDownload = false };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var result = await this.controller.ToggleSequentialDownload("hash1");

        result.Should().BeOfType<ContentResult>();
        torrent1.SequentialDownload.Should().BeTrue();
        await this.torrentService.Received(1).UpdateAsync(torrent1);
        await this.downloadEngine.Received(1).SetSequentialDownloadAsync(1, true);
    }

    [Test]
    public async Task ToggleSequentialDownload_WithEmptyHashes_ReturnsBadRequest()
    {
        var result = await this.controller.ToggleSequentialDownload(string.Empty);

        result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public async Task ToggleFirstLastPiecePrio_WithHashesAll_CallsEngineAndReturnsOk()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", FirstLastPiecePriority = false, SequentialDownload = false };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1 });

        var result = await this.controller.ToggleFirstLastPiecePrio("all");

        result.Should().BeOfType<ContentResult>();
        torrent1.FirstLastPiecePriority.Should().BeTrue();
        torrent1.SequentialDownload.Should().BeFalse();
        await this.torrentService.Received(1).UpdateAsync(torrent1);
        await this.downloadEngine.Received(1).SetFirstLastPiecePriorityAsync(1, true);
    }

    [Test]
    public async Task ToggleFirstLastPiecePrio_WithEmptyHashes_ReturnsBadRequest()
    {
        var result = await this.controller.ToggleFirstLastPiecePrio(string.Empty);

        result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public async Task RenameTorrent_ValidParameters_RenamesTorrentAndUpdates()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "Old Name" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = await this.controller.RenameTorrent("hash1", "New Name");

        result.Should().BeOfType<ContentResult>();
        torrent.Name.Should().Be("New Name");
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task RenameTorrent_TorrentNotFound_ReturnsNotFound()
    {
        this.torrentService.GetByInfoHash("nonexistent").Returns((Torrent)null);

        var result = await this.controller.RenameTorrent("nonexistent", "New Name");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task RenameTorrent_EmptyParameters_ReturnsBadRequest()
    {
        var result1 = await this.controller.RenameTorrent(string.Empty, "New Name");
        var result2 = await this.controller.RenameTorrent("hash1", string.Empty);

        result1.Should().BeOfType<BadRequestResult>();
        result2.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public async Task SetSequentialDownload_ValidParameters_UpdatesTorrentAndEngine()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", SequentialDownload = false, FirstLastPiecePriority = false };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var result = await this.controller.SetSequentialDownload("hash1", enable: true);

        result.Should().BeOfType<ContentResult>();
        torrent1.SequentialDownload.Should().BeTrue();
        torrent1.FirstLastPiecePriority.Should().BeFalse();
        await this.torrentService.Received(1).UpdateAsync(torrent1);
        await this.downloadEngine.Received(1).SetSequentialDownloadAsync(1, true);
    }

    [Test]
    public async Task SetFirstLastPiecePrio_ValidParameters_UpdatesTorrentAndEngine()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", SequentialDownload = false, FirstLastPiecePriority = false };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var result = await this.controller.SetFirstLastPiecePrio("hash1", enable: true);

        result.Should().BeOfType<ContentResult>();
        torrent1.FirstLastPiecePriority.Should().BeTrue();
        torrent1.SequentialDownload.Should().BeFalse();
        await this.torrentService.Received(1).UpdateAsync(torrent1);
        await this.downloadEngine.Received(1).SetFirstLastPiecePriorityAsync(1, true);
    }

    [Test]
    public void SetPiecePriority_ValidParameters_SetsPickerPriority()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.Picker.Returns(new PiecePicker(5, 16384, 5 * 16384));
        this.downloadEngine.GetTask(1).Returns(mockTask);

        var result = this.controller.SetPiecePriority(hash: "hash1", piece: 2, priority: 6);

        result.Should().BeOfType<ContentResult>();
        mockTask.Picker.GetPiecePriority(2).Should().Be(6);
    }

    [Test]
    public void GetPieceHashes_NonExistentTorrent_ReturnsNotFound()
    {
        this.torrentService.GetByInfoHash("nonexistent").Returns((Torrent)null);

        var result = this.controller.GetPieceHashes("nonexistent");

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void GetPieceHashes_ExistingTorrent_ReturnsPieceHashes()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", PieceCount = 3 };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var result = this.controller.GetPieceHashes("hash1");

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result;
        var list = okResult.Value as List<string>;
        list.Should().NotBeNull();
        list!.Count.Should().Be(3);
    }

    [Test]
    public void QBitTorrentSnapshot_FromTorrent_MapsAllPropertiesCorrectly()
    {
        var addedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var completedDate = new DateTime(2025, 1, 1, 13, 0, 0, DateTimeKind.Utc);
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Movie.2025",
            InfoHash = "abc123hash",
            TotalSize = 1000L,
            Downloaded = 400L,
            Uploaded = 800L,
            DownloadSpeed = 50L,
            UploadSpeed = 25L,
            Progress = 0.4,
            Status = TorrentStatus.Downloading,
            Category = "movies",
            Label = "tag1, tag2",
            Ratio = 2.0,
            Seeders = 10,
            Leechers = 5,
            DateAdded = addedDate,
            DateCompleted = completedDate,
            SequentialDownload = true,
            FirstLastPiecePriority = true,
        };

        var snapshot = QBitTorrentSnapshot.FromTorrent(torrent, "/downloads/movies", "/downloads/movies/Test.Movie.2025");

        snapshot.Name.Should().Be("Test.Movie.2025");
        snapshot.Size.Should().Be(1000L);
        snapshot.Progress.Should().Be(0.4);
        snapshot.DlSpeed.Should().Be(50L);
        snapshot.UpSpeed.Should().Be(25L);
        snapshot.State.Should().Be("downloading");
        snapshot.Category.Should().Be("movies");
        snapshot.Tags.Should().Be("tag1, tag2");
        snapshot.SavePath.Should().Be("/downloads/movies");
        snapshot.ContentPath.Should().Be("/downloads/movies/Test.Movie.2025");
        snapshot.Ratio.Should().Be(2.0);
        snapshot.NumSeeds.Should().Be(10);
        snapshot.NumLeechs.Should().Be(5);
        snapshot.Downloaded.Should().Be(400L);
        snapshot.Uploaded.Should().Be(800L);
        snapshot.AmountLeft.Should().Be(600L);
        snapshot.AddedOn.Should().Be(new DateTimeOffset(addedDate).ToUnixTimeSeconds());
        snapshot.CompletionOn.Should().Be(new DateTimeOffset(completedDate).ToUnixTimeSeconds());
        snapshot.SeqDl.Should().BeTrue();
        snapshot.FLPiecePrio.Should().BeTrue();
    }

    [Test]
    public void QBitTorrentSnapshot_FromTorrent_NullTorrent_ThrowsArgumentNullException()
    {
        var action = () => QBitTorrentSnapshot.FromTorrent(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void GetPieceStates_WithDownloadingPieces_MarksState1()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns(new[] { true, false, false, true });
        downloadTask.PartialPieces.Returns(new[] { 1 });
        downloadEngine.GetTask(1).Returns(downloadTask);

        var controllerWithEngine = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine);

        var torrent = new Torrent { Id = 1, InfoHash = "hash_partial", Name = "T1" };
        this.torrentService.GetByInfoHash("hash_partial").Returns(torrent);

        var result = controllerWithEngine.GetPieceStates("hash_partial");
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var states = okResult.Value.Should().BeOfType<List<int>>().Subject;

        states.Should().Equal(2, 1, 0, 2);
    }

    [Test]
    public void GetPieceStates_CompletedTorrent_WithNullBitfield_ReturnsTwos()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns((bool[])null);
        downloadEngine.GetTask(1).Returns(downloadTask);

        var controllerWithEngine = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine);

        var completedTorrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash_completed",
            Name = "Completed Torrent",
            Status = TorrentStatus.Completed,
            PieceCount = 4,
        };
        this.torrentService.GetByInfoHash("hash_completed").Returns(completedTorrent);

        var result = controllerWithEngine.GetPieceStates("hash_completed");
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var states = okResult.Value.Should().BeOfType<List<int>>().Subject;

        states.Should().Equal(2, 2, 2, 2);
    }

    [Test]
    public void GetPieceStates_SeedingTorrent_WithNullBitfield_ReturnsTwos()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns((bool[])null);
        downloadEngine.GetTask(2).Returns(downloadTask);

        var controllerWithEngine = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine);

        var seedingTorrent = new Torrent
        {
            Id = 2,
            InfoHash = "hash_seeding",
            Name = "Seeding Torrent",
            Status = TorrentStatus.Seeding,
            PieceCount = 3,
        };
        this.torrentService.GetByInfoHash("hash_seeding").Returns(seedingTorrent);

        var result = controllerWithEngine.GetPieceStates("hash_seeding");
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var states = okResult.Value.Should().BeOfType<List<int>>().Subject;

        states.Should().Equal(2, 2, 2);
    }

    [Test]
    public void GetPieceStates_DownloadingTorrent_WithNullBitfield_ReturnsZerosAndOnes()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns((bool[])null);
        downloadTask.PartialPieces.Returns(new[] { 1 });
        downloadEngine.GetTask(3).Returns(downloadTask);

        var controllerWithEngine = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            downloadEngine: downloadEngine);

        var downloadingTorrent = new Torrent
        {
            Id = 3,
            InfoHash = "hash_downloading",
            Name = "Downloading Torrent",
            Status = TorrentStatus.Downloading,
            PieceCount = 3,
        };
        this.torrentService.GetByInfoHash("hash_downloading").Returns(downloadingTorrent);

        var result = controllerWithEngine.GetPieceStates("hash_downloading");
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var states = okResult.Value.Should().BeOfType<List<int>>().Subject;

        states.Should().Equal(0, 1, 0);
    }

    [Test]
    public void GetPieceHashes_WithAppFolderInfo_UsesAppDataFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var torrentsDir = Path.Combine(tempDir, "Torrents");
        Directory.CreateDirectory(torrentsDir);

        try
        {
            var appFolderInfo = Substitute.For<IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            var torrentFileBytes = new byte[] { 1, 2, 3, 4 };
            var fakeHashes = new byte[40];
            Array.Fill(fakeHashes, (byte)0xab);
            File.WriteAllBytes(Path.Combine(torrentsDir, "hash_with_file.torrent"), torrentFileBytes);

            this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(new ParsedTorrent
            {
                PieceHashes = fakeHashes,
            });

            var controllerWithAppFolder = new QBittorrentApiController(
                this.torrentService,
                this.torrentFileService,
                this.torrentFileParser,
                this.categoryService,
                this.configService,
                this.trackerEntryRepository,
                appFolderInfo: appFolderInfo);

            var torrent = new Torrent { Id = 1, InfoHash = "hash_with_file", Name = "T1", PieceCount = 2 };
            this.torrentService.GetByInfoHash("hash_with_file").Returns(torrent);

            var result = controllerWithAppFolder.GetPieceHashes("hash_with_file");
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var list = okResult.Value.Should().BeOfType<List<string>>().Subject;

            list.Should().HaveCount(2);
            list[0].Should().Be(Convert.ToHexString(fakeHashes, 0, 20).ToLowerInvariant());
            list[1].Should().Be(Convert.ToHexString(fakeHashes, 20, 20).ToLowerInvariant());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private class TestableRingBufferTarget : RingBufferTarget
    {
        public TestableRingBufferTarget(int capacity = 2048)
            : base(capacity)
        {
        }

        public void WriteLog(LogLevel level, string logger, string message)
        {
            var logEvent = new LogEventInfo(level, logger, message)
            {
                TimeStamp = DateTime.UtcNow,
            };

            this.Write(logEvent);
        }
    }

    [Test]
    public void GetLogMain_ReturnsFilteredEntries()
    {
        var target = new TestableRingBufferTarget(10);
        target.WriteLog(LogLevel.Info, "TestLogger", "Message 1");
        target.WriteLog(LogLevel.Warn, "TestLogger", "Message 2");
        target.WriteLog(LogLevel.Error, "TestLogger", "Message 3");
        target.WriteLog(LogLevel.Debug, "TestLogger", "Message 4");

        var previousTarget = RingBufferTarget.Instance;
        RingBufferTarget.Instance = target;

        try
        {
            var resultAll = this.controller.GetLogMain();
            var okResult = resultAll.Result.Should().BeOfType<OkObjectResult>().Subject;
            var json = JsonSerializer.Serialize(okResult.Value);
            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement.EnumerateArray().ToList();
            array.Should().HaveCount(4);

            array[0].GetProperty("type").GetInt32().Should().Be(2); // Info
            array[0].GetProperty("message").GetString().Should().Be("Message 1");
            array[1].GetProperty("type").GetInt32().Should().Be(4); // Warning
            array[2].GetProperty("type").GetInt32().Should().Be(8); // Critical
            array[3].GetProperty("type").GetInt32().Should().Be(1); // Normal

            var resultInfoOnly = this.controller.GetLogMain(normal: false, info: true, warning: false, critical: false);
            var okInfoResult = resultInfoOnly.Result.Should().BeOfType<OkObjectResult>().Subject;
            var jsonInfo = JsonSerializer.Serialize(okInfoResult.Value);
            using var docInfo = JsonDocument.Parse(jsonInfo);
            var arrayInfo = docInfo.RootElement.EnumerateArray().ToList();
            arrayInfo.Should().HaveCount(1);
            arrayInfo[0].GetProperty("type").GetInt32().Should().Be(2);

            var firstId = array[0].GetProperty("id").GetInt32();
            var resultSinceFirst = this.controller.GetLogMain(last_known_id: firstId);
            var okSinceResult = resultSinceFirst.Result.Should().BeOfType<OkObjectResult>().Subject;
            var jsonSince = JsonSerializer.Serialize(okSinceResult.Value);
            using var docSince = JsonDocument.Parse(jsonSince);
            var arraySince = docSince.RootElement.EnumerateArray().ToList();
            arraySince.Should().HaveCount(3);
        }
        finally
        {
            RingBufferTarget.Instance = previousTarget;
        }
    }

    [Test]
    public void GetLogPeers_ReturnsBlockedPeers()
    {
        var peerService = Substitute.For<IPeerConnectionHistoryService>();
        var now = DateTime.UtcNow;
        var records = new List<PeerConnectionEvent>
        {
            new() { Id = 1, RemoteIp = "1.2.3.4", EventType = "Blocked", Timestamp = now },
            new() { Id = 2, RemoteIp = "5.6.7.8", EventType = "Connected", Timestamp = now },
            new() { Id = 3, RemoteIp = "9.10.11.12", EventType = "Rejected", Timestamp = now },
        };
        peerService.GetRecords().Returns(records);

        var ctrlWithPeers = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            peerConnectionHistoryService: peerService);

        var result = ctrlWithPeers.GetLogPeers(last_known_id: -1);
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement.EnumerateArray().ToList();

        array.Should().HaveCount(2);
        array[0].GetProperty("id").GetInt32().Should().Be(1);
        array[0].GetProperty("ip").GetString().Should().Be("1.2.3.4");
        array[0].GetProperty("blocked").GetBoolean().Should().BeTrue();
        array[1].GetProperty("id").GetInt32().Should().Be(3);
        array[1].GetProperty("ip").GetString().Should().Be("9.10.11.12");
        array[1].GetProperty("blocked").GetBoolean().Should().BeTrue();

        var resultFiltered = ctrlWithPeers.GetLogPeers(last_known_id: 1);
        var okFiltered = resultFiltered.Result.Should().BeOfType<OkObjectResult>().Subject;
        var jsonFiltered = JsonSerializer.Serialize(okFiltered.Value);
        using var docFiltered = JsonDocument.Parse(jsonFiltered);
        var arrayFiltered = docFiltered.RootElement.EnumerateArray().ToList();

        arrayFiltered.Should().HaveCount(1);
        arrayFiltered[0].GetProperty("id").GetInt32().Should().Be(3);
    }

    [Test]
    public void GetLogPeers_WhenNoHistoryService_ReturnsEmptyList()
    {
        var result = this.controller.GetLogPeers();
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Test]
    public void SearchEndpoints_FormParameters_HaveExplicitFromFormNameAttributes()
    {
        var methods = typeof(QBittorrentApiController).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        var startMethod = methods.First(m => m.Name == nameof(QBittorrentApiController.StartSearch));
        AssertFromFormName(startMethod, "formPattern", "pattern");
        AssertFromFormName(startMethod, "formPlugins", "plugins");
        AssertFromFormName(startMethod, "formCategory", "category");

        var stopMethod = methods.First(m => m.Name == nameof(QBittorrentApiController.StopSearch));
        AssertFromFormName(stopMethod, "formId", "id");

        var statusMethod = methods.First(m => m.Name == nameof(QBittorrentApiController.GetSearchStatus));
        AssertFromFormName(statusMethod, "formId", "id");

        var resultsMethod = methods.First(m => m.Name == nameof(QBittorrentApiController.GetSearchResults));
        AssertFromFormName(resultsMethod, "formId", "id");
        AssertFromFormName(resultsMethod, "formLimit", "limit");
        AssertFromFormName(resultsMethod, "formOffset", "offset");

        var deleteMethod = methods.First(m => m.Name == nameof(QBittorrentApiController.DeleteSearch));
        AssertFromFormName(deleteMethod, "formId", "id");

        static void AssertFromFormName(MethodInfo method, string parameterName, string expectedFormName)
        {
            var param = method.GetParameters().FirstOrDefault(p => p.Name == parameterName);
            param.Should().NotBeNull($"Parameter '{parameterName}' should exist on {method.Name}");
            var attr = param!.GetCustomAttribute<FromFormAttribute>();
            attr.Should().NotBeNull($"Parameter '{parameterName}' should have [FromForm]");
            attr!.Name.Should().Be(expectedFormName, $"Parameter '{parameterName}' on {method.Name} must map from form field '{expectedFormName}'");
        }
    }

    [Test]
    public void SearchEndpoints_WithFormParameters_DelegatesToSearchService()
    {
        var searchService = Substitute.For<IQBittorrentSearchService>();
        searchService.StartSearch("archlinux", "plugin1", "iso").Returns(42);
        searchService.GetStatus(42).Returns(new QBittorrentSearchStatus { Id = 42, Status = "Running", Total = 10 });
        searchService.GetResults(42, 5, -2).Returns(new QBittorrentSearchResultsResponse
        {
            Results = new List<QBittorrentSearchResultItem> { new() { FileName = "arch.iso" } },
            Status = "Running",
            Total = 10,
        });

        var customController = new QBittorrentApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.trackerEntryRepository,
            configFileProvider: this.configFileProvider,
            qbittorrentSearchService: searchService);

        var startResult = customController.StartSearch(formPattern: "archlinux", formPlugins: "plugin1", formCategory: "iso");
        var okStart = startResult.Should().BeOfType<OkObjectResult>().Subject;
        var startJson = JsonSerializer.Serialize(okStart.Value);
        using (var doc = JsonDocument.Parse(startJson))
        {
            doc.RootElement.GetProperty("id").GetInt32().Should().Be(42);
        }

        var statusResult = customController.GetSearchStatus(id: null, formId: 42);
        var okStatus = statusResult.Should().BeOfType<OkObjectResult>().Subject;
        var statuses = okStatus.Value.Should().BeAssignableTo<IEnumerable<QBittorrentSearchStatus>>().Subject.ToList();
        statuses.Should().HaveCount(1);
        statuses[0].Id.Should().Be(42);

        var resultsResult = customController.GetSearchResults(id: null, formId: 42, formLimit: 5, formOffset: -2);
        var okResults = resultsResult.Should().BeOfType<OkObjectResult>().Subject;
        var resultsObj = okResults.Value.Should().BeOfType<QBittorrentSearchResultsResponse>().Subject;
        resultsObj.Results.Should().HaveCount(1);
        resultsObj.Results[0].FileName.Should().Be("arch.iso");

        var stopResult = customController.StopSearch(id: null, formId: 42);
        stopResult.Should().BeOfType<ContentResult>();
        searchService.Received(1).StopSearch(42);

        var deleteResult = customController.DeleteSearch(id: null, formId: 42);
        deleteResult.Should().BeOfType<ContentResult>();
        searchService.Received(1).DeleteSearch(42);
    }

    [Test]
    public void GetSearchResults_WhenJobDoesNotExist_ReturnsNotFound()
    {
        var result = this.controller.GetSearchResults(id: 99999);
        result.Should().BeOfType<NotFoundResult>();

        var nullResult = this.controller.GetSearchResults(id: null);
        nullResult.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task AddTorrents_WithEmptyRequest_ReturnsFails()
    {
        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest());
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("Fails.");
        content.ContentType.Should().Be("text/plain");
    }

    [Test]
    public async Task AddTorrents_WithNullRequest_ReturnsFails()
    {
        var result = await this.controller.AddTorrents(null);
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("Fails.");
        content.ContentType.Should().Be("text/plain");
    }

    [Test]
    public async Task AddTorrents_WithOversizedFile_SkipsAndReturnsFails()
    {
        this.configService.MaxTorrentFileSizeBytes.Returns(100);
        var formFile = Substitute.For<IFormFile>();
        formFile.Length.Returns(500);

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Torrents = new List<IFormFile> { formFile },
        });

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("Fails.");
        await this.torrentService.DidNotReceiveWithAnyArgs().AddFromParsedTorrentAsync(default, default, default, default, default);
    }

    [Test]
    public async Task AddTorrents_WithCorruptedFile_CatchesExceptionAndReturnsFails()
    {
        var dummyBytes = new byte[] { 1, 2, 3 };
        var formFile = Substitute.For<IFormFile>();
        formFile.Length.Returns(dummyBytes.Length);
        formFile.CopyToAsync(Arg.Any<Stream>()).Returns(ci =>
        {
            var stream = ci.Arg<Stream>();
            stream.Write(dummyBytes, 0, dummyBytes.Length);
            return Task.CompletedTask;
        });

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(x => throw new InvalidOperationException("Invalid BEncoding"));

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Torrents = new List<IFormFile> { formFile },
        });

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("Fails.");
    }

    [Test]
    public async Task AddTorrents_WithFailedUrlDownload_ReturnsFails()
    {
        this.safeHttpClientService.DownloadBytesAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<byte[]>(new InvalidOperationException("Download error")));

        var result = await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "http://example.com/failed.torrent",
        });

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("Fails.");
    }

    [Test]
    public void QBitAddTorrentsRequest_HelperProperties_ParseFlagsCorrectly()
    {
        var req = new QBitAddTorrentsRequest
        {
            Skip_checking = "true",
            AutoTMM = "true",
            Root_folder = "true",
        };

        req.IsSkipChecking.Should().BeTrue();
        req.IsAutoTMM.Should().BeTrue();
        req.IsRootFolder.Should().BeTrue();

        var req2 = new QBitAddTorrentsRequest
        {
            SkipChecking = "1",
            AutoTmm = "1",
            RootFolder = "1",
        };

        req2.IsSkipChecking.Should().BeTrue();
        req2.IsAutoTMM.Should().BeTrue();
        req2.IsRootFolder.Should().BeTrue();

        var req3 = new QBitAddTorrentsRequest
        {
            Skip_checking = "false",
            AutoTMM = "false",
            Root_folder = "false",
        };

        req3.IsSkipChecking.Should().BeFalse();
        req3.IsAutoTMM.Should().BeFalse();
        req3.IsRootFolder.Should().BeFalse();
    }

    [Test]
    public void CreateCategory_WithValidCategory_CallsAddAndReturnsOk()
    {
        var result = this.controller.CreateCategory("movies", "/downloads/movies");

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("Ok.");
        this.categoryService.Received(1).Add(Arg.Is<Category>(c => c.Name == "movies" && c.SavePath == "/downloads/movies"));
    }

    [Test]
    public void CreateCategory_WithExistingCategoryWithoutSavePath_PassesEmptySavePathAndReturnsOk()
    {
        var result = this.controller.CreateCategory("tv", null);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("Ok.");
        this.categoryService.Received(1).Add(Arg.Is<Category>(c => c.Name == "tv" && c.SavePath == string.Empty));
    }

    [Test]
    public void CreateCategory_WithEmptyCategoryName_ReturnsBadRequest()
    {
        var result = this.controller.CreateCategory("   ", "/downloads");

        result.Should().BeOfType<BadRequestResult>();
        this.categoryService.DidNotReceive().Add(Arg.Any<Category>());
    }

    [Test]
    public async Task RenameFile_WithBareFilename_PreservesParentDirectoryStructure()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var file0 = new TorrentFile { Id = 101, TorrentId = 1, Path = "Season 1/Episode 01.mkv" };
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile> { file0 });
        this.torrentService.RenameFileAsync(1, "Season 1/Episode 01.mkv", "Season 1/Episode 01 - Pilot.mkv").Returns(Task.FromResult(true));

        var result = await this.controller.RenameFile("hash1", oldPath: "Season 1/Episode 01.mkv", newPath: "Episode 01 - Pilot.mkv");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).RenameFileAsync(1, "Season 1/Episode 01.mkv", "Season 1/Episode 01 - Pilot.mkv");
    }

    [Test]
    public async Task RenameFile_WithExplicitSubdirectory_DoesNotPrependParentDirectory()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);

        var file0 = new TorrentFile { Id = 101, TorrentId = 1, Path = "Season 1/Episode 01.mkv" };
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile> { file0 });
        this.torrentService.RenameFileAsync(1, "Season 1/Episode 01.mkv", "Season 2/Episode 01.mkv").Returns(Task.FromResult(true));

        var result = await this.controller.RenameFile("hash1", oldPath: "Season 1/Episode 01.mkv", newPath: "Season 2/Episode 01.mkv");

        result.Should().BeOfType<ContentResult>();
        await this.torrentService.Received(1).RenameFileAsync(1, "Season 1/Episode 01.mkv", "Season 2/Episode 01.mkv");
    }

    [Test]
    public void GetTorrentsInfo_And_GetMainData_HaveConsistentAmountLeft_WhenCompletedOrCrossSeeded()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "Torrent 1",
            TotalSize = 100_000_000,
            Progress = 1.0,
            Downloaded = 0,
            DateAdded = DateTime.UtcNow.AddHours(-1),
            Status = TorrentStatus.Seeding,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var infoResult = this.controller.GetTorrentsInfo();
        var okInfo = infoResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var infoList = okInfo.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        infoList[0]["amount_left"].Should().Be(0L);

        var mainDataResult = this.controller.GetMainData(0);
        var okMain = mainDataResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var mainDict = okMain.Value.Should().BeOfType<Dictionary<string, object>>().Subject;
        var torrents = mainDict["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        var json = JsonSerializer.Serialize(torrents["hash1"]);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("amount_left").GetInt64().Should().Be(0L);
    }

    [Test]
    public void GetTorrentsInfo_And_GetMainData_HaveConsistentAmountLeft_WhenInProgress()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash1",
            Name = "Torrent 1",
            TotalSize = 100_000_000,
            Progress = 0.6,
            Downloaded = 50_000_000,
            DateAdded = DateTime.UtcNow.AddHours(-1),
            Status = TorrentStatus.Downloading,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var infoResult = this.controller.GetTorrentsInfo();
        var okInfo = infoResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var infoList = okInfo.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        infoList[0]["amount_left"].Should().Be(40_000_000L);

        var mainDataResult = this.controller.GetMainData(0);
        var okMain = mainDataResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var mainDict = okMain.Value.Should().BeOfType<Dictionary<string, object>>().Subject;
        var torrents = mainDict["torrents"].Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        var json = JsonSerializer.Serialize(torrents["hash1"]);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("amount_left").GetInt64().Should().Be(40_000_000L);
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

    [Test]
    public async Task AddTrackers_WithMultitrackerTiers_PreservesTiersCorrectly()
    {
        var torrent = new Torrent
        {
            Id = 99,
            Name = "TierTorrent",
            InfoHash = "hash_tier_test",
            IsPrivate = false,
        };
        this.torrentService.GetByInfoHash("hash_tier_test").Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(99).Returns(new List<TrackerEntry>());

        var urls = "http://t0a\nhttp://t0b\n\nhttp://t1a\n\n\nhttp://t2a";
        var result = await this.controller.AddTrackers("hash_tier_test", urls);

        result.Should().BeOfType<ContentResult>();
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.Url == "http://t0a" && t.Tier == 0));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.Url == "http://t0b" && t.Tier == 0));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.Url == "http://t1a" && t.Tier == 1));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.Url == "http://t2a" && t.Tier == 2));
    }

    [Test]
    public async Task AddTrackers_WhenExistingTrackersPresent_CalculatesStartingTierCorrectly()
    {
        var torrent = new Torrent
        {
            Id = 100,
            Name = "ExistingTrackersTorrent",
            InfoHash = "hash_existing_tier",
            IsPrivate = false,
        };
        this.torrentService.GetByInfoHash("hash_existing_tier").Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(100).Returns(new List<TrackerEntry>
        {
            new TrackerEntry { TorrentId = 100, Url = "http://existing", Tier = 1 },
        });

        var urls = "http://new_t0\n\nhttp://new_t1";
        var result = await this.controller.AddTrackers("hash_existing_tier", urls);

        result.Should().BeOfType<ContentResult>();
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.Url == "http://new_t0" && t.Tier == 2));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.Url == "http://new_t1" && t.Tier == 3));
    }

    [Test]
    public void GetClientSessionKey_DifferentiatesClientsWithDifferentUserAgentHeaders()
    {
        var sonarrContext = new DefaultHttpContext();
        sonarrContext.Request.Headers["X-Api-Key"] = "shared_api_key";
        sonarrContext.Request.Headers["User-Agent"] = "Sonarr/4.0.13.2933";
        sonarrContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        this.controller.ControllerContext = new ControllerContext { HttpContext = sonarrContext };
        var sonarrKey = this.controller.GetClientSessionKey();

        var radarrContext = new DefaultHttpContext();
        radarrContext.Request.Headers["X-Api-Key"] = "shared_api_key";
        radarrContext.Request.Headers["User-Agent"] = "Radarr/5.19.3.9730";
        radarrContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        this.controller.ControllerContext = new ControllerContext { HttpContext = radarrContext };
        var radarrKey = this.controller.GetClientSessionKey();

        sonarrKey.Should().Be("key:shared_api_key:127.0.0.1:Sonarr");
        radarrKey.Should().Be("key:shared_api_key:127.0.0.1:Radarr");
        sonarrKey.Should().NotBe(radarrKey);
    }

    [Test]
    public void GetClientSessionKey_DifferentiatesClientsUsingClientIdOrArrInstanceHeaders()
    {
        var client1Context = new DefaultHttpContext();
        client1Context.Request.Headers["X-Api-Key"] = "shared_key";
        client1Context.Request.Headers["X-Client-Id"] = "sonarr-standard";
        client1Context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        this.controller.ControllerContext = new ControllerContext { HttpContext = client1Context };
        var client1Key = this.controller.GetClientSessionKey();

        var client2Context = new DefaultHttpContext();
        client2Context.Request.Headers["X-Api-Key"] = "shared_key";
        client2Context.Request.Headers["X-Arr-Instance"] = "sonarr-4k";
        client2Context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        this.controller.ControllerContext = new ControllerContext { HttpContext = client2Context };
        var client2Key = this.controller.GetClientSessionKey();

        var queryClientContext = new DefaultHttpContext();
        queryClientContext.Request.Headers["X-Api-Key"] = "shared_key";
        queryClientContext.Request.QueryString = new QueryString("?client_id=tab-session-99");
        queryClientContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        this.controller.ControllerContext = new ControllerContext { HttpContext = queryClientContext };
        var queryClientKey = this.controller.GetClientSessionKey();

        client1Key.Should().Be("key:shared_key:127.0.0.1:sonarr-standard");
        client2Key.Should().Be("key:shared_key:127.0.0.1:sonarr-4k");
        queryClientKey.Should().Be("key:shared_key:127.0.0.1:tab-session-99");
    }

    [Test]
    public void GetMainData_MaintainsIsolatedRidSequencesForDifferentClients()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            InfoHash = "hash1",
            Status = TorrentStatus.Downloading,
            Progress = 0.2,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var sonarrContext = new DefaultHttpContext();
        sonarrContext.Request.Headers["X-Api-Key"] = "shared_api_key";
        sonarrContext.Request.Headers["User-Agent"] = "Sonarr/4.0";
        sonarrContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        var radarrContext = new DefaultHttpContext();
        radarrContext.Request.Headers["X-Api-Key"] = "shared_api_key";
        radarrContext.Request.Headers["User-Agent"] = "Radarr/5.0";
        radarrContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        // Client 1 (Sonarr) initial full sync (rid = 0) -> receives rid 1
        this.controller.ControllerContext = new ControllerContext { HttpContext = sonarrContext };
        var sonarrRes1 = ((OkObjectResult)this.controller.GetMainData(0).Result!).Value as Dictionary<string, object>;
        sonarrRes1!["full_update"].Should().Be(true);
        sonarrRes1["rid"].Should().Be(1);

        // Client 1 (Sonarr) increments to rid 2
        var sonarrRes2 = ((OkObjectResult)this.controller.GetMainData(1).Result!).Value as Dictionary<string, object>;
        sonarrRes2!["rid"].Should().Be(2);

        // Client 2 (Radarr) starts fresh at rid 0 -> should receive rid 1 and full_update, without resetting Sonarr session
        this.controller.ControllerContext = new ControllerContext { HttpContext = radarrContext };
        var radarrRes1 = ((OkObjectResult)this.controller.GetMainData(0).Result!).Value as Dictionary<string, object>;
        radarrRes1!["full_update"].Should().Be(true);
        radarrRes1["rid"].Should().Be(1);

        // Client 1 (Sonarr) requests next delta with rid 2 -> should advance to rid 3, isolated from Radarr!
        this.controller.ControllerContext = new ControllerContext { HttpContext = sonarrContext };
        var sonarrRes3 = ((OkObjectResult)this.controller.GetMainData(2).Result!).Value as Dictionary<string, object>;
        sonarrRes3!["rid"].Should().Be(3);
    }

    [TestCase("Forced", 1)]
    [TestCase("forceEncrypted", 1)]
    [TestCase("RequireEncrypted", 1)]
    [TestCase("ForcedEncryption", 1)]
    [TestCase("Required", 1)]
    [TestCase("Disabled", 2)]
    [TestCase("Plaintext", 2)]
    [TestCase("None", 2)]
    [TestCase("preferEncrypted", 0)]
    [TestCase("Enabled", 0)]
    [TestCase("Unknown", 0)]
    public void GetPreferences_MapsEncryptionModeToExpectedPref(string mode, int expectedPref)
    {
        this.configService.EncryptionMode.Returns(mode);

        var actionResult = this.controller.GetPreferences();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dict = okResult.Value.Should().BeAssignableTo<IReadOnlyDictionary<string, object>>().Subject;

        dict["encryption"].Should().Be(expectedPref);
    }

    [TestCase(1, "forceEncrypted")]
    [TestCase(2, "disabled")]
    [TestCase(0, "preferEncrypted")]
    public async Task SetPreferencesAsync_UpdatesEncryptionMode(int encVal, string expectedMode)
    {
        var actionResult = await this.controller.SetPreferencesAsync($"{{\"encryption\":{encVal}}}");
        actionResult.Should().BeOfType<ContentResult>();

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["EncryptionMode"] == expectedMode));
    }

    [Test]
    public async Task CreateTorrent_WhenFallbackTorrentCreationServiceUsed_AllowsPathInsideConfiguredDownloadDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_qbit_create_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "video.mp4");
            await File.WriteAllBytesAsync(sourceFile, new byte[2048]);
            var outputFile = Path.Combine(tempDir, "video.torrent");

            this.configService.DownloadDir.Returns(tempDir);

            var ctrl = new QBittorrentApiController(
                this.torrentService,
                this.torrentFileService,
                this.torrentFileParser,
                this.categoryService,
                this.configService,
                this.trackerEntryRepository,
                configFileProvider: this.configFileProvider,
                safeHttpClientService: this.safeHttpClientService,
                downloadEngine: this.downloadEngine,
                diskProvider: this.diskProvider);

            var result = await ctrl.CreateTorrent(sourceFile, output_path: outputFile);

            result.Should().BeOfType<OkObjectResult>();
            File.Exists(outputFile).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task CreateTorrent_WhenPathIsOutsideAllowedDirectories_Returns500WithAllowedDirectoriesError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_qbit_allowed_" + Guid.NewGuid().ToString("N"));
        var outsideDir = Path.Combine(Path.GetTempPath(), "leecharr_qbit_outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(outsideDir);
        try
        {
            var sourceFile = Path.Combine(outsideDir, "secret.mp4");
            await File.WriteAllBytesAsync(sourceFile, new byte[2048]);

            this.configService.DownloadDir.Returns(tempDir);

            var ctrl = new QBittorrentApiController(
                this.torrentService,
                this.torrentFileService,
                this.torrentFileParser,
                this.categoryService,
                this.configService,
                this.trackerEntryRepository,
                configFileProvider: this.configFileProvider,
                safeHttpClientService: this.safeHttpClientService,
                downloadEngine: this.downloadEngine,
                diskProvider: this.diskProvider);

            var result = await ctrl.CreateTorrent(sourceFile);

            var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
            statusResult.StatusCode.Should().Be(500);
            statusResult.Value.ToString().Should().Contain("allowed storage directories");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            if (Directory.Exists(outsideDir))
            {
                Directory.Delete(outsideDir, true);
            }
        }
    }

    [Test]
    public async Task AddPeers_WithValidHashesAndPeers_ReturnsOkWithAddedCounts()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "Torrent 1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2", Name = "Torrent 2" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);
        this.torrentService.GetByInfoHash("hash2").Returns(torrent2);

        var result = await this.controller.AddPeers(hashes: "hash1|hash2", peers: "1.2.3.4:6881|5.6.7.8:51413");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dict = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;
        dict.Should().ContainKey("hash1");
        dict.Should().ContainKey("hash2");

        var hash1Stats = dict["hash1"];
        var added1 = (int)hash1Stats.GetType().GetProperty("added")!.GetValue(hash1Stats)!;
        var failed1 = (int)hash1Stats.GetType().GetProperty("failed")!.GetValue(hash1Stats)!;
        added1.Should().Be(2);
        failed1.Should().Be(0);

        await this.downloadEngine.Received(1).AddPeersAsync(1, Arg.Any<IEnumerable<string>>());
        await this.downloadEngine.Received(1).AddPeersAsync(2, Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task AddPeers_WithInvalidPeers_CountsFailures()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1", Name = "Torrent 1" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent1);

        var result = await this.controller.AddPeers(hashes: "hash1", peers: "1.2.3.4:6881|not-an-ip|999.999.999.999:1234|5.6.7.8:0");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dict = okResult.Value.Should().BeOfType<Dictionary<string, object>>().Subject;
        dict.Should().ContainKey("hash1");

        var hash1Stats = dict["hash1"];
        var added = (int)hash1Stats.GetType().GetProperty("added")!.GetValue(hash1Stats)!;
        var failed = (int)hash1Stats.GetType().GetProperty("failed")!.GetValue(hash1Stats)!;
        added.Should().Be(1);
        failed.Should().Be(3);
    }

    [Test]
    public async Task AddPeers_WithMissingHashesOrPeers_ReturnsBadRequest()
    {
        var result1 = await this.controller.AddPeers(hashes: null, peers: "1.2.3.4:6881");
        result1.Should().BeOfType<BadRequestResult>();

        var result2 = await this.controller.AddPeers(hashes: "hash1", peers: string.Empty);
        result2.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public async Task AddPeers_WhenTorrentNotFound_ReturnsNotFound()
    {
        this.torrentService.GetByInfoHash("unknown").Returns((Torrent)null);

        var result = await this.controller.AddPeers(hashes: "unknown", peers: "1.2.3.4:6881");
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task AddTrackers_WithBatchHashes_AddsTrackersToAllTorrents()
    {
        var torrent1 = new Torrent { Id = 10, InfoHash = "hash10", Name = "T10", IsPrivate = false };
        var torrent2 = new Torrent { Id = 20, InfoHash = "hash20", Name = "T20", IsPrivate = false };
        this.torrentService.GetByInfoHash("hash10").Returns(torrent1);
        this.torrentService.GetByInfoHash("hash20").Returns(torrent2);
        this.trackerEntryRepository.GetByTorrentId(10).Returns(new List<TrackerEntry>());
        this.trackerEntryRepository.GetByTorrentId(20).Returns(new List<TrackerEntry>());

        var result = await this.controller.AddTrackers(hash: "hash10|hash20", urls: "http://tracker1.org/announce\nhttp://tracker2.org/announce");

        result.Should().BeOfType<ContentResult>();
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 10 && t.Url == "http://tracker1.org/announce"));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 10 && t.Url == "http://tracker2.org/announce"));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 20 && t.Url == "http://tracker1.org/announce"));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 20 && t.Url == "http://tracker2.org/announce"));
        await this.downloadEngine.Received(1).AddTrackersAsync(10, Arg.Any<IEnumerable<string>>());
        await this.downloadEngine.Received(1).AddTrackersAsync(20, Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task AddTrackers_WithUrlEncodedTrackers_UnescapesUrlsBeforeAdding()
    {
        var torrent = new Torrent { Id = 30, InfoHash = "hash30", Name = "T30", IsPrivate = false };
        this.torrentService.GetByInfoHash("hash30").Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(30).Returns(new List<TrackerEntry>());

        var result = await this.controller.AddTrackers(hash: "hash30", urls: "http%3A%2F%2Ftracker3.org%2Fannounce");

        result.Should().BeOfType<ContentResult>();
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 30 && t.Url == "http://tracker3.org/announce"));
        await this.downloadEngine.Received(1).AddTrackersAsync(30, Arg.Is<List<string>>(l => l.Contains("http://tracker3.org/announce")));
    }

    [Test]
    public async Task RemoveTrackers_WithBatchHashesAndUrlEncodedTrackers_RemovesMatchingTrackers()
    {
        var torrent1 = new Torrent { Id = 41, InfoHash = "hash41", Name = "T41" };
        var torrent2 = new Torrent { Id = 42, InfoHash = "hash42", Name = "T42" };
        this.torrentService.GetByInfoHash("hash41").Returns(torrent1);
        this.torrentService.GetByInfoHash("hash42").Returns(torrent2);

        this.trackerEntryRepository.GetByTorrentId(41).Returns(new List<TrackerEntry>
        {
            new TrackerEntry { Id = 101, TorrentId = 41, Url = "http://tracker-rm.org/announce" },
        });
        this.trackerEntryRepository.GetByTorrentId(42).Returns(new List<TrackerEntry>
        {
            new TrackerEntry { Id = 102, TorrentId = 42, Url = "http://tracker-rm.org/announce" },
        });

        var result = await this.controller.RemoveTrackers(hash: "hash41|hash42", urls: "http%3A%2F%2Ftracker-rm.org%2Fannounce");

        result.Should().BeOfType<ContentResult>();
        this.trackerEntryRepository.Received(1).Delete(101);
        this.trackerEntryRepository.Received(1).Delete(102);
        await this.downloadEngine.Received(1).RemoveTrackersAsync(41, Arg.Any<HashSet<string>>());
        await this.downloadEngine.Received(1).RemoveTrackersAsync(42, Arg.Any<HashSet<string>>());
    }

    [Test]
    public void GetTorrentsInfo_WithSortingAndReversal_SortsResultsCorrectly()
    {
        var t1 = new Torrent { Id = 1, InfoHash = "h1", Name = "Alpha", TotalSize = 300, Progress = 0.3, DownloadSpeed = 100, UploadSpeed = 10, DateAdded = DateTime.UtcNow.AddMinutes(-30) };
        var t2 = new Torrent { Id = 2, InfoHash = "h2", Name = "Gamma", TotalSize = 100, Progress = 0.9, DownloadSpeed = 300, UploadSpeed = 30, DateAdded = DateTime.UtcNow.AddMinutes(-10) };
        var t3 = new Torrent { Id = 3, InfoHash = "h3", Name = "Beta", TotalSize = 200, Progress = 0.5, DownloadSpeed = 200, UploadSpeed = 20, DateAdded = DateTime.UtcNow.AddMinutes(-20) };
        this.torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3 });

        var resName = this.controller.GetTorrentsInfo(sort: "name");
        var listName = resName.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listName.Select(d => d["name"].ToString()).Should().ContainInOrder("Alpha", "Beta", "Gamma");

        var resNameDesc = this.controller.GetTorrentsInfo(sort: "name", reverse: true);
        var listNameDesc = resNameDesc.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listNameDesc.Select(d => d["name"].ToString()).Should().ContainInOrder("Gamma", "Beta", "Alpha");

        var resSize = this.controller.GetTorrentsInfo(sort: "size");
        var listSize = resSize.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listSize.Select(d => (long)d["size"]).Should().ContainInOrder(100L, 200L, 300L);

        var resSpeed = this.controller.GetTorrentsInfo(sort: "dlspeed", reverse: true);
        var listSpeed = resSpeed.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listSpeed.Select(d => (long)d["dlspeed"]).Should().ContainInOrder(300L, 200L, 100L);
    }

    [Test]
    public void GetTorrentsInfo_WithPagination_HonorsLimitAndPositiveOrNegativeOffset()
    {
        var torrents = Enumerable.Range(1, 10).Select(i => new Torrent
        {
            Id = i,
            InfoHash = $"h{i}",
            Name = $"T{i:D2}",
        }).ToList();
        this.torrentService.GetAll().Returns(torrents);

        var res1 = this.controller.GetTorrentsInfo(offset: 2, limit: 3);
        var list1 = res1.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list1.Select(d => d["name"].ToString()).Should().Equal("T03", "T04", "T05");

        var res2 = this.controller.GetTorrentsInfo(offset: -3);
        var list2 = res2.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        list2.Select(d => d["name"].ToString()).Should().Equal("T08", "T09", "T10");
    }

    [Test]
    public void GetTorrentsInfo_WithFilterSeedingVsCompletedAndQueued_DifferentiatesAccurately()
    {
        var activeSeeding = new Torrent { Id = 1, InfoHash = "h1", Status = TorrentStatus.Seeding, Progress = 1.0, UploadSpeed = 100 };
        var pausedCompleted = new Torrent { Id = 2, InfoHash = "h2", Status = TorrentStatus.Paused, Progress = 1.0, UploadSpeed = 0 };
        var queuedSeeding = new Torrent { Id = 3, InfoHash = "h3", Status = TorrentStatus.Queued, Progress = 1.0 };
        var queuedDownloading = new Torrent { Id = 4, InfoHash = "h4", Status = TorrentStatus.Queued, Progress = 0.2 };
        var queuedChecking = new Torrent { Id = 5, InfoHash = "h5", Status = TorrentStatus.QueuedForChecking, Progress = 0.5 };

        this.torrentService.GetAll().Returns(new List<Torrent> { activeSeeding, pausedCompleted, queuedSeeding, queuedDownloading, queuedChecking });

        var resSeeding = this.controller.GetTorrentsInfo(filter: "seeding");
        var listSeeding = resSeeding.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listSeeding.Select(d => d["hash"].ToString()).Should().BeEquivalentTo(new[] { "h1", "h3" });

        var resCompleted = this.controller.GetTorrentsInfo(filter: "completed");
        var listCompleted = resCompleted.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listCompleted.Select(d => d["hash"].ToString()).Should().BeEquivalentTo(new[] { "h1", "h2", "h3" });

        var resQueued = this.controller.GetTorrentsInfo(filter: "queued");
        var listQueued = resQueued.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<List<Dictionary<string, object>>>().Subject;
        listQueued.Select(d => d["hash"].ToString()).Should().BeEquivalentTo(new[] { "h3", "h4", "h5" });
    }

    [Test]
    public async Task AddTorrents_WithRenameAndTransferLimits_AppliesCorrectly()
    {
        var addedTorrent = new Torrent { Id = 10, InfoHash = "hash10", Name = "Original Name" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash10", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(addedTorrent);

        var request = new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash10",
            Rename = "Custom Renamed Torrent",
            UpLimit = 204800,
            DlLimit = 1048576,
        };

        var result = await this.controller.AddTorrents(request);

        result.Should().BeOfType<ContentResult>();
        addedTorrent.Name.Should().Be("Custom Renamed Torrent");
        addedTorrent.UploadLimit.Should().Be(200);
        addedTorrent.DownloadLimit.Should().Be(1024);
        await this.torrentService.Received(1).UpdateAsync(addedTorrent);
    }

    [Test]
    public void CreateCategory_WhenCategoryAlreadyExists_UpdatesExistingSavePathWithoutDuplicateInsert()
    {
        var existing = new Category { Id = 12, Name = "movies", SavePath = "/downloads/old" };
        this.categoryService.GetByName("movies").Returns(existing);

        var result = this.controller.CreateCategory("movies", "/downloads/new");

        result.Should().BeOfType<ContentResult>();
        this.categoryService.DidNotReceive().Add(Arg.Any<Category>());
        existing.SavePath.Should().Be("/downloads/new");
        this.categoryService.Received(1).Update(existing);
    }

    [Test]
    public async Task AddTags_PreservesAndUnionsExistingTagsWithoutOverwriting()
    {
        var torrent = new Torrent
        {
            Id = 51,
            InfoHash = "hash51",
            Name = "T51",
            Label = "tagA, tagB",
        };
        this.torrentService.GetByInfoHash("hash51").Returns(torrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = await this.controller.AddTags("hash51", " tagB , tagC , tagD ");
        result.Should().BeOfType<ContentResult>();

        torrent.Label.Should().Be("tagA, tagB, tagC, tagD");
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task RemoveTags_ParsesCommaSeparatedAndRemovesSpecifiedTagsWithoutCorruptingRemaining()
    {
        var torrent = new Torrent
        {
            Id = 52,
            InfoHash = "hash52",
            Name = "T52",
            Label = "alpha, beta, gamma, delta",
        };
        this.torrentService.GetByInfoHash("hash52").Returns(torrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = await this.controller.RemoveTags("hash52", " beta , delta ");
        result.Should().BeOfType<ContentResult>();

        torrent.Label.Should().Be("alpha, gamma");
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public void GetMainData_FullAndDeltaUpdates_IncludeTagsAndCategoriesDeltas()
    {
        var cat1 = new Category { Id = 1, Name = "tv", SavePath = "/downloads/tv" };
        var cat2 = new Category { Id = 2, Name = "movies", SavePath = "/downloads/movies" };
        var categories = new List<Category> { cat1, cat2 };
        this.categoryService.GetAll().Returns(_ => categories);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "h1",
            Name = "T1",
            Label = "tag1, tag2",
        };
        var torrents = new List<Torrent> { torrent };
        this.torrentService.GetAll().Returns(_ => torrents);

        var fullRes = this.controller.GetMainData(0);
        var fullData = ((OkObjectResult)fullRes.Result!).Value as Dictionary<string, object>;
        fullData!["full_update"].Should().Be(true);
        fullData["categories_removed"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().BeEmpty();
        fullData["tags_removed"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().BeEmpty();
        fullData["tags"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().BeEquivalentTo(new[] { "tag1", "tag2" });

        var initialRid = (int)fullData["rid"];

        categories = new List<Category> { cat1 };
        torrent.Label = "tag1, tag2, tag3";

        var deltaRes = this.controller.GetMainData(initialRid);
        var deltaData = ((OkObjectResult)deltaRes.Result!).Value as Dictionary<string, object>;
        deltaData!["full_update"].Should().Be(false);
        deltaData["categories_removed"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().BeEquivalentTo(new[] { "movies" });
        deltaData["tags"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().BeEquivalentTo(new[] { "tag3" });
        deltaData["tags_removed"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().BeEmpty();

        var deltaRid = (int)deltaData["rid"];

        torrent.Label = "tag1, tag3";

        var deltaRes2 = this.controller.GetMainData(deltaRid);
        var deltaData2 = ((OkObjectResult)deltaRes2.Result!).Value as Dictionary<string, object>;
        deltaData2!["full_update"].Should().Be(false);
        deltaData2["tags_removed"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().BeEquivalentTo(new[] { "tag2" });
    }

    [Test]
    public void GetMainData_WithSidCookie_IsolatesRidSequencePerSession()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "h1", Name = "T1" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var session1Context = new DefaultHttpContext();
        session1Context.Request.Headers["Cookie"] = "SID=session_alpha";
        session1Context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");

        var session2Context = new DefaultHttpContext();
        session2Context.Request.Headers["Cookie"] = "SID=session_beta";
        session2Context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");

        this.controller.ControllerContext = new ControllerContext { HttpContext = session1Context };
        var res1 = ((OkObjectResult)this.controller.GetMainData(0).Result!).Value as Dictionary<string, object>;
        res1!["rid"].Should().Be(1);

        var res2 = ((OkObjectResult)this.controller.GetMainData(1).Result!).Value as Dictionary<string, object>;
        res2!["rid"].Should().Be(2);

        this.controller.ControllerContext = new ControllerContext { HttpContext = session2Context };
        var resSession2 = ((OkObjectResult)this.controller.GetMainData(0).Result!).Value as Dictionary<string, object>;
        resSession2!["rid"].Should().Be(1);

        this.controller.ControllerContext = new ControllerContext { HttpContext = session1Context };
        var resSession1Next = ((OkObjectResult)this.controller.GetMainData(2).Result!).Value as Dictionary<string, object>;
        resSession1Next!["rid"].Should().Be(3);
    }

    [Test]
    public async Task SetShareLimits_And_GetTorrentsInfo_OperateSeedingTimeLimitNativelyInMinutes()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1", Name = "T1", TargetSeedTimeMinutes = 0 };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var setRes = await this.controller.SetShareLimits("hash1", seedingTimeLimit: 120);
        setRes.Should().BeOfType<ContentResult>();
        torrent.TargetSeedTimeMinutes.Should().Be(120);

        var infoRes = this.controller.GetTorrentsInfo();
        var okResult = infoRes.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Subject.ToList();

        list.Should().HaveCount(1);
        list[0]["seeding_time_limit"].Should().Be(120);
        list[0]["max_seeding_time"].Should().Be(120);

        var newTorrent = new Torrent { Id = 2, InfoHash = "hash2", Name = "T2" };
        this.torrentService.AddFromMagnetAsync("magnet:?xt=urn:btih:hash2", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(newTorrent);

        await this.controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:hash2",
            SeedingTimeLimit = 120,
        });

        newTorrent.TargetSeedTimeMinutes.Should().Be(120);
    }

    [Test]
    public async Task EditTracker_WithUrlParameterAlias_UpdatesTrackerCorrectly()
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

        var result = await ctrl.EditTracker("hash1", url: "http://tracker1.org/announce", newUrl: "http://tracker2.org/announce");

        result.Should().BeOfType<ContentResult>();
        tracker.Url.Should().Be("http://tracker2.org/announce");
        this.trackerEntryRepository.Received(1).Update(tracker);
        await downloadEngine.Received(1).RemoveTrackersAsync(1, Arg.Is<HashSet<string>>(s => s.Contains("http://tracker1.org/announce")));
        await downloadEngine.Received(1).AddTrackersAsync(1, Arg.Is<List<string>>(l => l.Contains("http://tracker2.org/announce")));
    }

    [Test]
    public async Task EditTracker_WhenTrackerNotFound_Returns409Conflict()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "hash1", TrackerUrl = "http://tracker1.org/announce" };
        this.torrentService.GetByInfoHash("hash1").Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(1).Returns(new List<TrackerEntry>());

        var result = await this.controller.EditTracker("hash1", origUrl: "http://nonexistent-tracker.org/announce", newUrl: "http://tracker2.org/announce");

        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        objResult.Value.Should().Be("Tracker not found.");
    }
}
