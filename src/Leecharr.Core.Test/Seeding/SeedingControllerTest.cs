// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Seeding;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Seeding;

[TestFixture]
public class SeedingControllerTest
{
    private ITorrentService torrentService = null!;
    private SeedingController controller = null!;

    [SetUp]
    public void SetUp()
    {
        SeedingController.ResetHistory();
        this.torrentService = Substitute.For<ITorrentService>();
        this.controller = new SeedingController(this.torrentService);
    }

    [Test]
    public void GetStats_ReturnsCorrectAggregatedStats()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 1000,
            UploadSpeed = 200,
            Downloaded = 5000,
            Uploaded = 1000,
            Seeders = 5,
            Leechers = 3,
            Ratio = 0.2,
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Status = TorrentStatus.Seeding,
            DownloadSpeed = 0,
            UploadSpeed = 800,
            Downloaded = 10000,
            Uploaded = 15000,
            Seeders = 10,
            Leechers = 2,
            Ratio = 1.5,
        };
        var torrent3 = new Torrent
        {
            Id = 3,
            Status = TorrentStatus.Paused,
            DownloadSpeed = 0,
            UploadSpeed = 0,
            Downloaded = 2000,
            Uploaded = 0,
            Seeders = 0,
            Leechers = 0,
            Ratio = 0.0,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2, torrent3 });

        var actionResult = this.controller.GetStats();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var stats = okResult.Value.Should().BeOfType<SeedingStatsResource>().Subject;

        stats.ActiveTorrents.Should().Be(2);
        stats.DownloadingTorrents.Should().Be(1);
        stats.SeedingTorrents.Should().Be(1);
        stats.PausedTorrents.Should().Be(1);
        stats.DownloadSpeed.Should().Be(1000);
        stats.UploadSpeed.Should().Be(1000);
        stats.TotalDownloaded.Should().Be(17000);
        stats.TotalUploaded.Should().Be(16000);
        stats.GlobalRatio.Should().BeApproximately(16000.0 / 17000.0, 0.0001);
        stats.AverageRatio.Should().BeApproximately((0.2 + 1.5 + 0.0) / 3.0, 0.0001);
    }

    [Test]
    public void GetStats_EmptyTorrents_ReturnsZeroes()
    {
        this.torrentService.GetAll().Returns(new List<Torrent>());

        var actionResult = this.controller.GetStats();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var stats = okResult.Value.Should().BeOfType<SeedingStatsResource>().Subject;

        stats.ActiveTorrents.Should().Be(0);
        stats.DownloadingTorrents.Should().Be(0);
        stats.SeedingTorrents.Should().Be(0);
        stats.PausedTorrents.Should().Be(0);
        stats.DownloadSpeed.Should().Be(0);
        stats.UploadSpeed.Should().Be(0);
        stats.TotalDownloaded.Should().Be(0);
        stats.TotalUploaded.Should().Be(0);
        stats.GlobalRatio.Should().Be(0.0);
        stats.AverageRatio.Should().Be(0.0);
    }

    [Test]
    public void GetHistory_PopulatesAndReturnsTelemetrySnapshots()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 500,
            UploadSpeed = 1500,
            Downloaded = 100,
            Uploaded = 200,
            Seeders = 4,
            Leechers = 6,
            Ratio = 2.0,
        };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = this.controller.GetHistory();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var history = okResult.Value.Should().BeOfType<List<SpeedSnapshotResource>>().Subject;

        history.Should().HaveCount(1);
        var snapshot = history[0];
        snapshot.DownloadSpeed.Should().Be(500);
        snapshot.UploadSpeed.Should().Be(1500);
        snapshot.ActiveTorrents.Should().Be(1);
        snapshot.TotalPeers.Should().Be(10);
        snapshot.AverageRatio.Should().Be(2.0);
    }

    [Test]
    public void GetTorrentHistory_ExistingTorrent_ReturnsHistory()
    {
        var torrent = new Torrent
        {
            Id = 42,
            DownloadSpeed = 300,
            UploadSpeed = 700,
            Status = TorrentStatus.Downloading,
        };

        this.torrentService.Get(42).Returns(torrent);
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = this.controller.GetTorrentHistory(42);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var history = okResult.Value.Should().BeOfType<List<TorrentSpeedSnapshotResource>>().Subject;

        history.Should().NotBeEmpty();
        history[0].TorrentId.Should().Be(42);
        history[0].DownloadSpeed.Should().Be(300);
        history[0].UploadSpeed.Should().Be(700);
    }

    [Test]
    public void GetTorrentHistory_NonExistentTorrent_ReturnsNotFound()
    {
        this.torrentService.Get(999).Returns((Torrent)null!);

        var actionResult = this.controller.GetTorrentHistory(999);
        actionResult.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task Start_ResumesTorrent()
    {
        var actionResult = await this.controller.Start(10);
        actionResult.Should().BeOfType<OkResult>();
        await this.torrentService.Received(1).ResumeAsync(10);
    }

    [Test]
    public async Task Stop_PausesTorrent()
    {
        var actionResult = await this.controller.Stop(10);
        actionResult.Should().BeOfType<OkResult>();
        await this.torrentService.Received(1).PauseAsync(10);
    }

    [Test]
    public async Task StartAll_ResumesAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1 };
        var torrent2 = new Torrent { Id = 2 };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var actionResult = await this.controller.StartAll();
        actionResult.Should().BeOfType<OkResult>();
        await this.torrentService.Received(1).ResumeAsync(1);
        await this.torrentService.Received(1).ResumeAsync(2);
    }

    [Test]
    public async Task StopAll_PausesAllTorrents()
    {
        var torrent1 = new Torrent { Id = 1 };
        var torrent2 = new Torrent { Id = 2 };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var actionResult = await this.controller.StopAll();
        actionResult.Should().BeOfType<OkResult>();
        await this.torrentService.Received(1).PauseAsync(1);
        await this.torrentService.Received(1).PauseAsync(2);
    }
}
