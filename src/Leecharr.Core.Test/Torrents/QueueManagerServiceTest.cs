// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
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
        task1.DownloadSpeed.Returns(1000L); // Slow! < 5000 * 1024
        this.downloadEngine.GetTask(1).Returns(task1);

        var task2 = Substitute.For<IDownloadTask>();
        task2.DownloadSpeed.Returns(10000000L); // Fast: 10MB/s > 5000 * 1024
        this.downloadEngine.GetTask(2).Returns(task2);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "Slow", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "Fast", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 2 },
            new Torrent { Id = 3, Name = "NextInQueue", Status = TorrentStatus.Queued, Progress = 0.3, QueuePosition = 3 },
        };

        this.torrentRepository.All().Returns(torrents);

        for (var i = 0; i < 3; i++)
        {
            await this.queueManager.ProcessQueueAsync();
        }

        // Since T1 is slow and ignored after hysteresis ticks, T2 takes 1 active slot, so T3 also fits within MaxActiveDownloads=2!
        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        torrents[2].Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(3);
    }

    [Test]
    public async Task ProcessQueueAsync_OrdersQueueByPriorityDescending()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "LowPriorityFirstInQueue", Status = TorrentStatus.Queued, Progress = 0.0, Priority = 0, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "HighPriorityLaterInQueue", Status = TorrentStatus.Queued, Progress = 0.0, Priority = 2, QueuePosition = 2 },
            new Torrent { Id = 3, Name = "NormalPriorityMiddleInQueue", Status = TorrentStatus.Queued, Progress = 0.0, Priority = 1, QueuePosition = 3 },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        // High priority torrent (Id = 2) should be promoted first despite having a higher QueuePosition
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        torrents[0].Status.Should().Be(TorrentStatus.Queued);
        torrents[2].Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(1);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(3);
    }

    [Test]
    public async Task ProcessQueueAsync_RequiresConsecutiveSlowTicksBeforeIgnoringSlowTorrent()
    {
        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(5000L);
        this.configService.MaxActiveDownloads.Returns(1);

        var task1 = Substitute.For<IDownloadTask>();
        task1.DownloadSpeed.Returns(1000L); // Below threshold (slow)
        this.downloadEngine.GetTask(1).Returns(task1);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "Slow", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "Next", Status = TorrentStatus.Queued, Progress = 0.2, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        // Tick 1: Slow, but only 1 tick (threshold is 3). Should NOT promote Next.
        await this.queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Queued);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(2);

        // Tick 2: Slow, 2 ticks. Should still NOT promote Next.
        await this.queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Queued);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(2);

        // Tick 3: Slow, 3 consecutive ticks. Now it should be ignored and Next promoted!
        await this.queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
    }

    [Test]
    public async Task ProcessQueueAsync_ResetsConsecutiveSlowTicksOnSpeedRecovery()
    {
        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(5000L);
        this.configService.MaxActiveDownloads.Returns(1);

        var task1 = Substitute.For<IDownloadTask>();
        this.downloadEngine.GetTask(1).Returns(task1);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "Fluctuating", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "Next", Status = TorrentStatus.Queued, Progress = 0.2, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        // Tick 1 & 2: Below threshold
        task1.DownloadSpeed.Returns(1000L);
        await this.queueManager.ProcessQueueAsync();
        await this.queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Queued);

        // Tick 3: Speed recovers above threshold (10 MB/s > 5000 * 1024)!
        task1.DownloadSpeed.Returns(10000000L);
        await this.queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Queued);

        // Tick 4: Speed drops again (this is only tick 1 of the new streak). Should NOT promote Next.
        task1.DownloadSpeed.Returns(1000L);
        await this.queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Queued);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(2);
    }

    [Test]
    public async Task ProcessQueueAsync_ActiveCooldownPreventsImmediateDemotionOnSpeedFluctuations()
    {
        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(5000L);
        this.configService.MaxActiveDownloads.Returns(1);

        var task1 = Substitute.For<IDownloadTask>();
        task1.DownloadSpeed.Returns(1000L); // Slow: < 5000 * 1024
        this.downloadEngine.GetTask(1).Returns(task1);

        var task2 = Substitute.For<IDownloadTask>();
        task2.DownloadSpeed.Returns(20000000L);
        this.downloadEngine.GetTask(2).Returns(task2);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "T2", Status = TorrentStatus.Queued, Progress = 0.2, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        // Run 3 ticks so T1 is marked slow and T2 is promoted to Downloading with ActivatedAt set
        for (var i = 0; i < 3; i++)
        {
            await this.queueManager.ProcessQueueAsync();
        }

        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);

        // Next tick: T1 suddenly recovers speed above threshold (10 MB/s > 5000 * 1024).
        // T1 is no longer slow, so slot 1 is consumed by T1.
        task1.DownloadSpeed.Returns(10000000L);

        // Run QueueManager: T2 is in its 30s active run cooldown, so it must NOT be demoted to Queued!
        await this.queueManager.ProcessQueueAsync();

        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        await this.downloadEngine.DidNotReceive().PauseTorrentAsync(2);
    }

    [Test]
    public async Task ProcessQueueAsync_DemotesExcessTorrentAfterActiveCooldownExpires()
    {
        // Use custom queue manager with zero cooldown window to simulate cooldown expiration
        var qmWithZeroCooldown = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 1,
            minimumActiveCooldown: TimeSpan.Zero);

        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(5000L);
        this.configService.MaxActiveDownloads.Returns(1);

        var task1 = Substitute.For<IDownloadTask>();
        task1.DownloadSpeed.Returns(1000L); // Slow
        this.downloadEngine.GetTask(1).Returns(task1);

        var task2 = Substitute.For<IDownloadTask>();
        task2.DownloadSpeed.Returns(20000000L);
        this.downloadEngine.GetTask(2).Returns(task2);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "T2", Status = TorrentStatus.Queued, Progress = 0.2, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        // Tick 1: T1 is slow, T2 is promoted to Downloading
        await qmWithZeroCooldown.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);

        // T1 recovers speed; since minimumActiveCooldown is Zero, T2 cooldown is already expired
        task1.DownloadSpeed.Returns(10000000L);

        await qmWithZeroCooldown.ProcessQueueAsync();

        torrents[1].Status.Should().Be(TorrentStatus.Queued);
        await this.downloadEngine.Received(1).PauseTorrentAsync(2);
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

    [Test]
    public async Task ProcessQueueAsync_WhenResumeThrows_DoesNotUpdateStatusOrDatabase()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Queued, Progress = 0.0, QueuePosition = 1 },
        };

        this.torrentRepository.All().Returns(torrents);
        this.downloadEngine.ResumeTorrentAsync(1).Returns(Task.FromException(new System.InvalidOperationException("Engine error")));

        await this.queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Queued);
        this.torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<TorrentStatusChangedEvent>());
    }

    [Test]
    public async Task ProcessQueueAsync_WhenPauseThrows_DoesNotUpdateStatusOrDatabase()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "T2", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);
        this.downloadEngine.PauseTorrentAsync(2).Returns(Task.FromException(new System.InvalidOperationException("Engine error")));

        await this.queueManager.ProcessQueueAsync();

        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        this.torrentRepository.DidNotReceive().Update(Arg.Is<Torrent>(t => t.Id == 2));
    }

    [Test]
    public async Task ProcessQueueAsync_WhenConcurrentCallsArriveWhileProcessing_CoalescesAndReEvaluatesQueue()
    {
        var runCount = 0;
        var firstRunStarted = new TaskCompletionSource<bool>();
        var allowFirstRunToProceed = new TaskCompletionSource<bool>();

        this.torrentRepository.All().Returns(_ =>
        {
            var currentRun = Interlocked.Increment(ref runCount);
            if (currentRun == 1)
            {
                firstRunStarted.TrySetResult(true);
                allowFirstRunToProceed.Task.GetAwaiter().GetResult();
            }

            return new List<Torrent>
            {
                new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.5 },
            };
        });

        // Launch first process run in background
        var firstTask = Task.Run(async () => await this.queueManager.ProcessQueueAsync());

        // Wait until first run is inside ProcessQueueInternalAsync
        await firstRunStarted.Task;

        // Trigger two concurrent invocations while the first is in progress
        var secondTask = this.queueManager.ProcessQueueAsync();
        var thirdTask = this.queueManager.ProcessQueueAsync();

        await Task.WhenAll(secondTask, thirdTask);

        // Allow first run to complete
        allowFirstRunToProceed.SetResult(true);
        await firstTask;

        // Verify coalescing re-run occurred
        runCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task ProcessQueueAsync_WhenDownloadingWithActiveSpeedAndLastActiveNull_PersistsLastActive()
    {
        var task = Substitute.For<IDownloadTask>();
        task.DownloadSpeed.Returns(500_000L);
        this.downloadEngine.GetTask(1).Returns(task);

        var torrent = new Torrent
        {
            Id = 1,
            Name = "ActiveDownload",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            DownloadSpeed = 500_000,
            LastActive = null,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        await this.queueManager.ProcessQueueAsync();

        torrent.LastActive.Should().NotBeNull();
        (DateTime.UtcNow - torrent.LastActive!.Value).TotalSeconds.Should().BeLessThan(5);
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 1 && t.LastActive.HasValue));
    }

    [Test]
    public async Task ProcessQueueAsync_WhenDownloadingWithActiveSpeedAndLastActiveOlderThan30Seconds_PersistsUpdatedLastActive()
    {
        var task = Substitute.For<IDownloadTask>();
        task.DownloadSpeed.Returns(500_000L);
        this.downloadEngine.GetTask(1).Returns(task);

        var staleTimestamp = DateTime.UtcNow.AddMinutes(-5);
        var torrent = new Torrent
        {
            Id = 1,
            Name = "ActiveDownloadStale",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            DownloadSpeed = 500_000,
            LastActive = staleTimestamp,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        await this.queueManager.ProcessQueueAsync();

        torrent.LastActive.Should().NotBe(staleTimestamp);
        (DateTime.UtcNow - torrent.LastActive!.Value).TotalSeconds.Should().BeLessThan(5);
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 1 && t.LastActive != staleTimestamp));
    }

    [Test]
    public async Task ProcessQueueAsync_WhenDownloadingWithActiveSpeedAndLastActiveFreshWithin30Seconds_ThrottlesUpdate()
    {
        var task = Substitute.For<IDownloadTask>();
        task.DownloadSpeed.Returns(500_000L);
        this.downloadEngine.GetTask(1).Returns(task);

        var recentTimestamp = DateTime.UtcNow.AddSeconds(-10);
        var torrent = new Torrent
        {
            Id = 1,
            Name = "ActiveDownloadRecent",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            DownloadSpeed = 500_000,
            LastActive = recentTimestamp,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        await this.queueManager.ProcessQueueAsync();

        this.torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public async Task ProcessQueueAsync_WhenSeedingWithActiveUploadSpeedAndLastActiveNull_PersistsLastActive()
    {
        var task = Substitute.For<IDownloadTask>();
        task.UploadSpeed.Returns(200_000L);
        this.downloadEngine.GetTask(1).Returns(task);

        var torrent = new Torrent
        {
            Id = 1,
            Name = "ActiveSeeder",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            UploadSpeed = 200_000,
            LastActive = null,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        await this.queueManager.ProcessQueueAsync();

        torrent.LastActive.Should().NotBeNull();
        (DateTime.UtcNow - torrent.LastActive!.Value).TotalSeconds.Should().BeLessThan(5);
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 1 && t.LastActive.HasValue));
    }

    [Test]
    public async Task ProcessQueueAsync_WhenTorrentHasNegativePriority_DoesNotPromoteFromQueued()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "DoNotDownload", Status = TorrentStatus.Queued, Progress = 0.0, Priority = -1, QueuePosition = 1 },
            new Torrent { Id = 2, Name = "NormalDownload", Status = TorrentStatus.Queued, Progress = 0.0, Priority = 0, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Queued);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(1);
        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenPausedTorrentResumedToDownloading_TriggersQueueEvaluation()
    {
        this.configService.MaxActiveDownloads.Returns(2);

        var t1 = new Torrent { Id = 1, Name = "HighPriority", Status = TorrentStatus.Downloading, Progress = 0.1, Priority = 2, QueuePosition = 1 };
        var t2 = new Torrent { Id = 2, Name = "LowPriority", Status = TorrentStatus.Downloading, Progress = 0.1, Priority = 1, QueuePosition = 2 };
        var t3 = new Torrent { Id = 3, Name = "HighestPriorityResumed", Status = TorrentStatus.Downloading, Progress = 0.1, Priority = 3, QueuePosition = 3 };

        var torrents = new List<Torrent> { t1, t2, t3 };
        this.torrentRepository.All().Returns(torrents);

        this.queueManager.Handle(new TorrentStatusChangedEvent
        {
            Torrent = t3,
            OldStatus = TorrentStatus.Paused,
            NewStatus = TorrentStatus.Downloading,
            IsQueueManagerInternal = false,
        });

        // Give background Task.Run time to complete
        await Task.Delay(250);

        t1.Status.Should().Be(TorrentStatus.Downloading);
        t3.Status.Should().Be(TorrentStatus.Downloading);
        t2.Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).PauseTorrentAsync(2);
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStalledTorrentRecoversToDownloading_TriggersQueueEvaluation()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var t1 = new Torrent { Id = 1, Name = "PromotedTorrent", Status = TorrentStatus.Downloading, Progress = 0.5, Priority = 1, QueuePosition = 1 };
        var t2 = new Torrent { Id = 2, Name = "RecoveredStalled", Status = TorrentStatus.Downloading, Progress = 0.2, Priority = 2, QueuePosition = 2 };

        var torrents = new List<Torrent> { t1, t2 };
        this.torrentRepository.All().Returns(torrents);

        this.queueManager.Handle(new TorrentStatusChangedEvent
        {
            Torrent = t2,
            OldStatus = TorrentStatus.Stalled,
            NewStatus = TorrentStatus.Downloading,
            IsQueueManagerInternal = false,
        });

        await Task.Delay(250);

        t2.Status.Should().Be(TorrentStatus.Downloading);
        t1.Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).PauseTorrentAsync(1);
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenTorrentTransitionsToQueued_TriggersQueueEvaluation()
    {
        this.configService.MaxActiveDownloads.Returns(2);

        var t1 = new Torrent { Id = 1, Name = "ActiveDownload", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 };
        var t2 = new Torrent { Id = 2, Name = "RequeuedFromPaused", Status = TorrentStatus.Queued, Progress = 0.0, QueuePosition = 2 };

        var torrents = new List<Torrent> { t1, t2 };
        this.torrentRepository.All().Returns(torrents);

        this.queueManager.Handle(new TorrentStatusChangedEvent
        {
            Torrent = t2,
            OldStatus = TorrentStatus.Paused,
            NewStatus = TorrentStatus.Queued,
            IsQueueManagerInternal = false,
        });

        await Task.Delay(250);

        t1.Status.Should().Be(TorrentStatus.Downloading);
        t2.Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStatusUnchanged_DoesNotTriggerQueueEvaluation()
    {
        var t1 = new Torrent { Id = 1, Name = "ActiveDownload", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 1 };

        this.queueManager.Handle(new TorrentStatusChangedEvent
        {
            Torrent = t1,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Downloading,
            IsQueueManagerInternal = false,
        });

        await Task.Delay(100);

        this.torrentRepository.DidNotReceive().All();
    }

    [Test]
    public async Task ProcessQueueAsync_WhenTorrentHasForceStart_PromotesDownloadingEvenWhenMaxDownloadsReached()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var t1 = new Torrent { Id = 1, Name = "RegularDownloading", Status = TorrentStatus.Downloading, Progress = 0.5, QueuePosition = 1 };
        var t2 = new Torrent { Id = 2, Name = "ForceStartQueued", Status = TorrentStatus.Queued, Progress = 0.1, QueuePosition = 2, ForceStart = true };

        var torrents = new List<Torrent> { t1, t2 };
        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        t1.Status.Should().Be(TorrentStatus.Downloading);
        t2.Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
        await this.downloadEngine.DidNotReceive().PauseTorrentAsync(1);
    }

    [Test]
    public async Task ProcessQueueAsync_WhenDownloadingTorrentHasForceStart_NeverDemotesToQueued()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var t1 = new Torrent { Id = 1, Name = "Normal1", Status = TorrentStatus.Downloading, Progress = 0.5, Priority = 10, QueuePosition = 1 };
        var t2 = new Torrent { Id = 2, Name = "ForceStartDownloading", Status = TorrentStatus.Downloading, Progress = 0.1, Priority = 1, QueuePosition = 2, ForceStart = true };

        var torrents = new List<Torrent> { t1, t2 };
        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        t1.Status.Should().Be(TorrentStatus.Downloading);
        t2.Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.DidNotReceive().PauseTorrentAsync(2);
    }

    [Test]
    public async Task ProcessQueueAsync_WhenTorrentHasForceStart_PromotesSeedingEvenWhenMaxUploadsReached()
    {
        this.configService.MaxActiveUploads.Returns(1);

        var t1 = new Torrent { Id = 1, Name = "RegularSeeding", Status = TorrentStatus.Seeding, Progress = 1.0, QueuePosition = 1 };
        var t2 = new Torrent { Id = 2, Name = "ForceStartSeedingQueued", Status = TorrentStatus.Queued, Progress = 1.0, QueuePosition = 2, ForceStart = true };

        var torrents = new List<Torrent> { t1, t2 };
        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        t1.Status.Should().Be(TorrentStatus.Seeding);
        t2.Status.Should().Be(TorrentStatus.Seeding);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
        await this.downloadEngine.DidNotReceive().PauseTorrentAsync(1);
    }

    [Test]
    public async Task ProcessQueueAsync_WhenDeadMagnetExceedsMetadataTimeout_DoesNotBlockQueuedTorrentsWithMetadata()
    {
        this.configService.MaxActiveDownloads.Returns(1);
        this.configService.MagnetMetadataTimeoutSeconds.Returns(180);

        var pastTime = DateTime.UtcNow.AddSeconds(-200);

        var deadMagnet = new Torrent
        {
            Id = 1,
            Name = "DeadMagnet",
            Status = TorrentStatus.Downloading,
            TotalSize = 0,
            PieceCount = 0,
            Progress = 0.0,
            QueuePosition = 1,
            DateAdded = pastTime,
            DownloadSpeed = 0,
        };

        var queuedWithMetadata = new Torrent
        {
            Id = 2,
            Name = "HealthyQueuedTorrent",
            Status = TorrentStatus.Queued,
            TotalSize = 1024 * 1024 * 100,
            PieceCount = 100,
            Progress = 0.0,
            QueuePosition = 2,
            DateAdded = DateTime.UtcNow,
            DownloadSpeed = 0,
        };

        var torrents = new List<Torrent> { deadMagnet, queuedWithMetadata };
        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        deadMagnet.Status.Should().Be(TorrentStatus.Queued);
        queuedWithMetadata.Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.Received(1).PauseTorrentAsync(1);
        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 1 && t.Status == TorrentStatus.Queued));
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 2 && t.Status == TorrentStatus.Downloading));
    }

    [Test]
    public async Task ProcessQueueAsync_WhenMagnetWithinMetadataTimeout_RemainsDownloadingAndBlocksQueuedTorrent()
    {
        this.configService.MaxActiveDownloads.Returns(1);
        this.configService.MagnetMetadataTimeoutSeconds.Returns(180);

        var recentTime = DateTime.UtcNow.AddSeconds(-30);

        var magnet = new Torrent
        {
            Id = 1,
            Name = "FreshMagnet",
            Status = TorrentStatus.Downloading,
            TotalSize = 0,
            PieceCount = 0,
            Progress = 0.0,
            QueuePosition = 1,
            DateAdded = recentTime,
            DownloadSpeed = 0,
        };

        var queuedWithMetadata = new Torrent
        {
            Id = 2,
            Name = "QueuedTorrent",
            Status = TorrentStatus.Queued,
            TotalSize = 1024 * 1024 * 100,
            PieceCount = 100,
            Progress = 0.0,
            QueuePosition = 2,
            DateAdded = DateTime.UtcNow,
            DownloadSpeed = 0,
        };

        var torrents = new List<Torrent> { magnet, queuedWithMetadata };
        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        magnet.Status.Should().Be(TorrentStatus.Downloading);
        queuedWithMetadata.Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.DidNotReceive().PauseTorrentAsync(1);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(2);
    }

    [Test]
    public async Task ProcessQueueAsync_WhenQueuedDeadMagnetExceedsMetadataTimeout_DoesNotPromoteToDownloading()
    {
        this.configService.MaxActiveDownloads.Returns(2);
        this.configService.MagnetMetadataTimeoutSeconds.Returns(180);

        var pastTime = DateTime.UtcNow.AddSeconds(-200);

        var deadMagnet = new Torrent
        {
            Id = 1,
            Name = "DeadQueuedMagnet",
            Status = TorrentStatus.Queued,
            TotalSize = 0,
            PieceCount = 0,
            Progress = 0.0,
            QueuePosition = 1,
            DateAdded = pastTime,
            DownloadSpeed = 0,
        };

        var queuedWithMetadata = new Torrent
        {
            Id = 2,
            Name = "HealthyQueuedTorrent",
            Status = TorrentStatus.Queued,
            TotalSize = 1024 * 1024 * 100,
            PieceCount = 100,
            Progress = 0.0,
            QueuePosition = 2,
            DateAdded = DateTime.UtcNow,
            DownloadSpeed = 0,
        };

        var torrents = new List<Torrent> { deadMagnet, queuedWithMetadata };
        this.torrentRepository.All().Returns(torrents);

        await this.queueManager.ProcessQueueAsync();

        deadMagnet.Status.Should().Be(TorrentStatus.Queued);
        queuedWithMetadata.Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(1);
        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
    }
}
