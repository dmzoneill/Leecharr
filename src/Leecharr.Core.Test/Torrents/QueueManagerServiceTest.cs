// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class QueueManagerServiceTest
{
    private ITorrentRepository torrentRepository = null!;
    private IConfigService configService = null!;
    private IDownloadEngine downloadEngine = null!;
    private IEventAggregator eventAggregator = null!;
    private QueueManagerService queueManager = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.configService = Substitute.For<IConfigService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.eventAggregator = Substitute.For<IEventAggregator>();

        this.configService.MaxActiveDownloads.Returns(2);
        this.configService.MaxActiveUploads.Returns(2);
        this.configService.MaxActiveTorrents.Returns(10);
        this.configService.IgnoreSlowTorrents.Returns(false);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(2048L);
        this.configService.SlowTorrentUploadRateThreshold.Returns(2048L);

        this.queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator);
    }

    [Test]
    public async Task ProcessQueueAsync_DemotesExcessDownloadsToQueued()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "T2", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 2 },
            new Torrent { Id = 3, Name = "T3", Status = TorrentStatus.Downloading, Progress = 0.3, QueuePosition = 3 },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        torrents[2].Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).PauseTorrentAsync(3);
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 3 && t.Status == TorrentStatus.Queued));
    }

    [Test]
    public async Task ProcessQueueAsync_PromotesQueuedTorrentWhenSlotAvailable()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "T2", Status = TorrentStatus.Queued, Progress = 0.2, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 2 && t.Status == TorrentStatus.Downloading));
    }

    [Test]
    public async Task ProcessQueueAsync_IgnoresSlowTorrentWhenConfigured()
    {
        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(5000L);

        var task1 = Substitute.For<IDownloadTask>();
        task1.DownloadSpeed.Returns(1000L); // Slow! < 5000
        this.downloadEngine.GetTask(1).Returns(task1);

        var task2 = Substitute.For<IDownloadTask>();
        task2.DownloadSpeed.Returns(100000L); // Fast
        this.downloadEngine.GetTask(2).Returns(task2);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "Slow", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "Fast", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 2 },
            new Torrent { Id = 3, Name = "NextInQueue", Status = TorrentStatus.Queued, Progress = 0.3, QueuePosition = 3 },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        // Since T1 is slow and ignored, T2 takes 1 active slot, so T3 also fits within MaxActiveDownloads=2!
        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        torrents[2].Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(3);
    }

    [Test]
    public async Task ProcessQueueAsync_NeverResumesExplicitlyPausedTorrents()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "PausedByUser", Status = TorrentStatus.Paused, Progress = 0.5, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "StoppedByUser", Status = TorrentStatus.Stopped, Progress = 0.8, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Paused);
        torrents[1].Status.Should().Be(TorrentStatus.Stopped);

        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(Arg.Any<int>());
    }

    [Test]
    public async Task ProcessQueueAsync_CompletedTorrentsDoNotConsumeActiveSlots()
    {
        this.configService.MaxActiveTorrents.Returns(1);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "Finished", Status = TorrentStatus.Completed, Progress = 1.0, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "QueuedDownload", Status = TorrentStatus.Queued, Progress = 0.0, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Completed);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
    }

    [Test]
    public async Task ProcessQueueAsync_QueuedTorrentsDoNotBypassLimitsWhenQueueStalledEnabled()
    {
        this.configService.MaxActiveDownloads.Returns(1);
        this.configService.QueueStalledEnabled.Returns(true);
        this.configService.QueueStalledMinutes.Returns(5);

        var pastTime = System.DateTime.UtcNow.AddMinutes(-30);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "ActiveDownload", Status = TorrentStatus.Downloading, Progress = 0.5, QueuePosition = 1, DateAdded = pastTime, LastActive = System.DateTime.UtcNow, DownloadSpeed = 10000 },
            new Torrent { Id = 2, Name = "QueuedOld1", Status = TorrentStatus.Queued, Progress = 0.0, QueuePosition = 2, DateAdded = pastTime, LastActive = pastTime },
            new Torrent { Id = 3, Name = "QueuedOld2", Status = TorrentStatus.Queued, Progress = 0.0, QueuePosition = 3, DateAdded = pastTime, LastActive = pastTime },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        // ActiveDownload should remain Downloading (1 active download slot consumed).
        // QueuedOld1 and QueuedOld2 must NOT be promoted to Downloading because maxActiveDownloads=1 is reached and Queued torrents are not stalled active downloads!
        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Queued);
        torrents[2].Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(2);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(3);
    }

    [Test]
    public async Task ProcessQueueAsync_QueuedSeedingTorrentsDoNotBypassLimitsWhenIdleSeedingLimitEnabled()
    {
        this.configService.MaxActiveUploads.Returns(1);
        this.configService.IdleSeedingLimitMinutes.Returns(5);

        var pastTime = System.DateTime.UtcNow.AddMinutes(-30);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "ActiveSeeder", Status = TorrentStatus.Seeding, Progress = 1.0, QueuePosition = 1, DateAdded = pastTime, DateCompleted = pastTime, LastActive = System.DateTime.UtcNow, UploadSpeed = 10000 },
            new Torrent { Id = 2, Name = "QueuedSeedOld1", Status = TorrentStatus.Queued, Progress = 1.0, QueuePosition = 2, DateAdded = pastTime, DateCompleted = pastTime, LastActive = pastTime },
            new Torrent { Id = 3, Name = "QueuedSeedOld2", Status = TorrentStatus.Queued, Progress = 1.0, QueuePosition = 3, DateAdded = pastTime, DateCompleted = pastTime, LastActive = pastTime },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        // ActiveSeeder should remain Seeding (1 active upload slot consumed).
        // QueuedSeedOld1 and QueuedSeedOld2 must NOT be promoted to Seeding because maxActiveUploads=1 is reached!
        torrents[0].Status.Should().Be(TorrentStatus.Seeding);
        torrents[1].Status.Should().Be(TorrentStatus.Queued);
        torrents[2].Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(2);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(3);
    }
}
