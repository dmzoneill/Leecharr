// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using CoreTorrent = NzbDrone.Core.Torrents.Torrent;

namespace Leecharr.Core.Test.Scheduling;

[TestFixture]
public class BandwidthScheduleAndQueueServiceTest
{
    private ISpeedScheduleRepository speedScheduleRepository = null!;
    private IConfigService configService = null!;
    private IDownloadEngine downloadEngine = null!;
    private IEventAggregator eventAggregator = null!;
    private IQueueManagerService queueManagerService = null!;
    private ITorrentRepository torrentRepository = null!;
    private SpeedSchedulerService speedScheduler = null!;

    [SetUp]
    public void SetUp()
    {
        this.speedScheduleRepository = Substitute.For<ISpeedScheduleRepository>();
        this.configService = Substitute.For<IConfigService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.queueManagerService = Substitute.For<IQueueManagerService>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();

        // Default global config values
        this.configService.MaxDownloadSpeedKbps.Returns(50000);
        this.configService.MaxUploadSpeedKbps.Returns(20000);
        this.configService.AlternativeSpeedEnabled.Returns(false);
        this.configService.AltDownloadSpeedKbps.Returns(5000);
        this.configService.AltUploadSpeedKbps.Returns(2000);
        this.configService.SchedulerEnabled.Returns(false);

        // Queue defaults
        this.configService.MaxActiveDownloads.Returns(2);
        this.configService.MaxActiveUploads.Returns(2);
        this.configService.MaxActiveTorrents.Returns(10);
        this.configService.DownloadQueueSize.Returns(0);
        this.configService.SeedQueueSize.Returns(0);
        this.configService.IgnoreSlowTorrents.Returns(false);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(2048L);
        this.configService.SlowTorrentUploadRateThreshold.Returns(2048L);
        this.configService.QueueStalledEnabled.Returns(false);
        this.configService.QueueStalledMinutes.Returns(15);
        this.configService.MagnetMetadataTimeoutSeconds.Returns(180);

        this.speedScheduler = new SpeedSchedulerService(
            this.speedScheduleRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            this.queueManagerService,
            this.torrentRepository);
    }

    [TearDown]
    public void TearDown()
    {
        this.speedScheduler?.Dispose();
    }

    #region Scheduled Speed Limits

    [Test]
    public void GetCurrentLimits_WhenScheduleMatches_ReturnsThrottledLimitsAndActiveFlag()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Name = "Daytime Limit",
                Days = 127, // All days
                StartTime = "08:00:00",
                EndTime = "18:00:00",
                MaxDownloadSpeed = 10000,
                MaxUploadSpeed = 3000,
                IsEnabled = true,
                Priority = 1,
            },
        };

        this.speedScheduleRepository.GetEnabled().Returns(schedules);

        var testTime = new DateTime(2026, 9, 21, 14, 30, 0); // Monday 14:30
        var limits = this.speedScheduler.GetCurrentLimits(testTime);

        limits.Should().NotBeNull();
        limits.MaxDownloadSpeedKbps.Should().Be(10000);
        limits.MaxUploadSpeedKbps.Should().Be(3000);
        limits.IsThrottled.Should().BeTrue();
        limits.HasActiveSchedule.Should().BeTrue();
        limits.IsPaused.Should().BeFalse();
    }

    [Test]
    public void GetCurrentLimits_WhenNoActiveSchedule_ReturnsConfiguredGlobalLimits()
    {
        this.speedScheduleRepository.GetEnabled().Returns(new List<SpeedSchedule>());

        var testTime = new DateTime(2026, 9, 21, 14, 30, 0);
        var limits = this.speedScheduler.GetCurrentLimits(testTime);

        limits.Should().NotBeNull();
        limits.MaxDownloadSpeedKbps.Should().Be(50000);
        limits.MaxUploadSpeedKbps.Should().Be(20000);
        limits.IsThrottled.Should().BeFalse();
        limits.HasActiveSchedule.Should().BeFalse();
    }

    [Test]
    public void GetCurrentLimits_WhenMultipleSchedulesOverlap_SelectsHighestPrioritySchedule()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Name = "Low Priority Broad Window",
                Days = 127,
                StartTime = "08:00:00",
                EndTime = "20:00:00",
                MaxDownloadSpeed = 15000,
                MaxUploadSpeed = 5000,
                IsEnabled = true,
                Priority = 5,
            },
            new()
            {
                Id = 2,
                Name = "High Priority Peak Window",
                Days = 127,
                StartTime = "12:00:00",
                EndTime = "14:00:00",
                MaxDownloadSpeed = 2000,
                MaxUploadSpeed = 1000,
                IsEnabled = true,
                Priority = 20,
            },
        };

        this.speedScheduleRepository.GetEnabled().Returns(schedules);

        // At 13:00, both schedules are active -> Priority 20 should win
        var limits = this.speedScheduler.GetCurrentLimits(new DateTime(2026, 9, 21, 13, 0, 0));

        limits.MaxDownloadSpeedKbps.Should().Be(2000);
        limits.MaxUploadSpeedKbps.Should().Be(1000);
        limits.IsThrottled.Should().BeTrue();
    }

    [Test]
    public void GetCurrentLimits_RespectsDaysOfWeekBitmask_MatchesDayOfWeekCorrectly()
    {
        // Monday=2, Tuesday=4, Wednesday=8, Thursday=16, Friday=32 -> Weekdays = 62
        var weekdaySchedule = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Name = "Weekday Throttling",
                Days = 62,
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 4000,
                MaxUploadSpeed = 1500,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.speedScheduleRepository.GetEnabled().Returns(weekdaySchedule);

        // Wednesday 10:00 (active weekday)
        var wednesday = new DateTime(2026, 9, 23, 10, 0, 0);
        var wedLimits = this.speedScheduler.GetCurrentLimits(wednesday);
        wedLimits.HasActiveSchedule.Should().BeTrue();
        wedLimits.MaxDownloadSpeedKbps.Should().Be(4000);

        // Sunday 10:00 (inactive weekend)
        var sunday = new DateTime(2026, 9, 20, 10, 0, 0);
        var sunLimits = this.speedScheduler.GetCurrentLimits(sunday);
        sunLimits.HasActiveSchedule.Should().BeFalse();
        sunLimits.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    #endregion

    #region Alternative Speed Windows (Turtle Mode)

    [Test]
    public void GetCurrentLimits_WhenAlternativeSpeedManuallyEnabled_OverridesSchedules()
    {
        this.configService.AlternativeSpeedEnabled.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(8000);
        this.configService.AltUploadSpeedKbps.Returns(3000);

        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Name = "Regular Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = 25000,
                MaxUploadSpeed = 10000,
                IsEnabled = true,
                Priority = 100,
            },
        };

        this.speedScheduleRepository.GetEnabled().Returns(schedules);

        var limits = this.speedScheduler.GetCurrentLimits(new DateTime(2026, 9, 21, 12, 0, 0));

        limits.MaxDownloadSpeedKbps.Should().Be(8000);
        limits.MaxUploadSpeedKbps.Should().Be(3000);
        limits.IsThrottled.Should().BeTrue();
        limits.HasActiveSchedule.Should().BeTrue();
    }

    [Test]
    public void GetCurrentLimits_WhenAlternativeSpeedIsNegative_SetsDownloadAndUploadPaused()
    {
        this.configService.AlternativeSpeedEnabled.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(-1);
        this.configService.AltUploadSpeedKbps.Returns(-1);

        var limits = this.speedScheduler.GetCurrentLimits(new DateTime(2026, 9, 21, 12, 0, 0));

        limits.IsDownloadPaused.Should().BeTrue();
        limits.IsUploadPaused.Should().BeTrue();
        limits.IsPaused.Should().BeTrue();
        limits.MaxDownloadSpeedKbps.Should().Be(0);
        limits.MaxUploadSpeedKbps.Should().Be(0);
    }

    [Test]
    public void GetCurrentLimits_WhenConfigSchedulerActive_AppliesAlternativeSpeedsInTimeWindow()
    {
        this.configService.AlternativeSpeedEnabled.Returns(false);
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(8);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(18);
        this.configService.SchedulerEndMinute.Returns(0);
        this.configService.SchedulerMonday.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(6000);
        this.configService.AltUploadSpeedKbps.Returns(2500);

        this.speedScheduleRepository.GetEnabled().Returns(new List<SpeedSchedule>());

        // Monday 11:00 (inside window)
        var inside = new DateTime(2026, 9, 21, 11, 0, 0);
        var insideLimits = this.speedScheduler.GetCurrentLimits(inside);
        insideLimits.HasActiveSchedule.Should().BeTrue();
        insideLimits.MaxDownloadSpeedKbps.Should().Be(6000);
        insideLimits.MaxUploadSpeedKbps.Should().Be(2500);

        // Monday 21:00 (outside window)
        var outside = new DateTime(2026, 9, 21, 21, 0, 0);
        var outsideLimits = this.speedScheduler.GetCurrentLimits(outside);
        outsideLimits.HasActiveSchedule.Should().BeFalse();
        outsideLimits.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    #endregion

    #region Time-Window Transitions

    [Test]
    public async Task ApplyCurrentLimitsAsync_EnteringPauseSchedule_PausesAllActiveTorrents_AndRecordsPausedIds()
    {
        var pauseSchedule = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = -1,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.speedScheduleRepository.GetEnabled().Returns(pauseSchedule);

        var task1 = Substitute.For<IDownloadTask>();
        task1.TorrentId.Returns(101);
        task1.Status.Returns(TorrentStatus.Downloading);

        var task2 = Substitute.For<IDownloadTask>();
        task2.TorrentId.Returns(102);
        task2.Status.Returns(TorrentStatus.Seeding);

        var task3 = Substitute.For<IDownloadTask>();
        task3.TorrentId.Returns(103);
        task3.Status.Returns(TorrentStatus.Paused); // Already manually paused

        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { task1, task2, task3 });

        var dbTorrents = new List<CoreTorrent>
        {
            new() { Id = 101, Status = TorrentStatus.Downloading },
            new() { Id = 102, Status = TorrentStatus.Seeding },
            new() { Id = 103, Status = TorrentStatus.Paused },
        };
        this.torrentRepository.All().Returns(dbTorrents);

        await this.speedScheduler.ApplyCurrentLimitsAsync();

        this.speedScheduler.WasPausedByScheduler.Should().BeTrue();
        this.speedScheduler.SchedulerPausedTorrentIds.Should().Contain(101);
        this.speedScheduler.SchedulerPausedTorrentIds.Should().Contain(102);
        this.speedScheduler.SchedulerPausedTorrentIds.Should().NotContain(103, "Manually paused torrents should not be recorded for scheduler resumption");

        await this.downloadEngine.Received(1).PauseAllAsync();
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_ExitingPauseSchedule_ResumesOnlySchedulerPausedTorrents_AndTriggersQueueManager()
    {
        // 1. Enter pause schedule
        var pauseSchedule = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = -1,
                IsEnabled = true,
            },
        };
        this.speedScheduleRepository.GetEnabled().Returns(pauseSchedule);

        var task1 = Substitute.For<IDownloadTask>();
        task1.TorrentId.Returns(201);
        task1.Status.Returns(TorrentStatus.Downloading);

        var task2 = Substitute.For<IDownloadTask>();
        task2.TorrentId.Returns(202);
        task2.Status.Returns(TorrentStatus.Downloading);

        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { task1, task2 });
        this.torrentRepository.All().Returns(new List<CoreTorrent>
        {
            new() { Id = 201, Status = TorrentStatus.Downloading },
            new() { Id = 202, Status = TorrentStatus.Downloading },
        });

        await this.speedScheduler.ApplyCurrentLimitsAsync();
        this.speedScheduler.WasPausedByScheduler.Should().BeTrue();

        // 2. Transition out of pause (revert to normal limits)
        this.speedScheduleRepository.GetEnabled().Returns(new List<SpeedSchedule>());

        // User manually changed Torrent 202 status to Paused while scheduler was paused
        var t1 = new CoreTorrent { Id = 201, Status = TorrentStatus.Downloading };
        var t2 = new CoreTorrent { Id = 202, Status = TorrentStatus.Paused };
        this.torrentRepository.Get(201).Returns(t1);
        this.torrentRepository.Get(202).Returns(t2);

        await this.speedScheduler.ApplyCurrentLimitsAsync();

        this.speedScheduler.WasPausedByScheduler.Should().BeFalse();
        this.speedScheduler.SchedulerPausedTorrentIds.Should().BeEmpty();

        await this.downloadEngine.Received(1).ResumeTorrentAsync(201);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(202);

        await this.queueManagerService.Received(1).ProcessQueueAsync();
        await this.downloadEngine.Received(1).SetRateLimitsAsync(50000, 20000);
    }

    [Test]
    public void GetCurrentLimits_WhenOvernightScheduleCrossesMidnight_MatchesBothLateNightAndEarlyMorning()
    {
        // Monday=2. Overnight: Monday 22:00 to 06:00
        var overnight = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Name = "Overnight Throttle",
                Days = 2, // Monday only
                StartTime = "22:00:00",
                EndTime = "06:00:00",
                MaxDownloadSpeed = 1000,
                MaxUploadSpeed = 500,
                IsEnabled = true,
            },
        };

        this.speedScheduleRepository.GetEnabled().Returns(overnight);

        // Monday 23:30 (before midnight)
        var lateMonday = new DateTime(2026, 9, 21, 23, 30, 0);
        var mondayLimits = this.speedScheduler.GetCurrentLimits(lateMonday);
        mondayLimits.HasActiveSchedule.Should().BeTrue();
        mondayLimits.MaxDownloadSpeedKbps.Should().Be(1000);

        // Tuesday 04:00 (after midnight, prevDay is Monday)
        var earlyTuesday = new DateTime(2026, 9, 22, 4, 0, 0);
        var tuesdayLimits = this.speedScheduler.GetCurrentLimits(earlyTuesday);
        tuesdayLimits.HasActiveSchedule.Should().BeTrue();
        tuesdayLimits.MaxDownloadSpeedKbps.Should().Be(1000);

        // Tuesday 10:00 (well after overnight window)
        var morningTuesday = new DateTime(2026, 9, 22, 10, 0, 0);
        var outsideLimits = this.speedScheduler.GetCurrentLimits(morningTuesday);
        outsideLimits.HasActiveSchedule.Should().BeFalse();
        outsideLimits.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    #endregion

    #region Queue Demotion for Stalled Downloads

    [Test]
    public async Task QueueManager_DemotesStalledTorrent_WhenQueueStalledThresholdExceeded()
    {
        this.configService.QueueStalledEnabled.Returns(true);
        this.configService.QueueStalledMinutes.Returns(15);
        this.configService.MaxActiveDownloads.Returns(1);

        var task = Substitute.For<IDownloadTask>();
        task.DownloadSpeed.Returns(0L);
        task.IsStalled.Returns(true);
        this.downloadEngine.GetTask(1).Returns(task);

        var torrent = new CoreTorrent
        {
            Id = 1,
            Name = "StalledDownload",
            Status = TorrentStatus.Downloading,
            Progress = 0.4,
            DownloadSpeed = 0,
            LastActive = DateTime.UtcNow.AddMinutes(-20),
            QueuePosition = 1,
        };

        var nextInQueue = new CoreTorrent
        {
            Id = 2,
            Name = "ActiveCandidate",
            Status = TorrentStatus.Queued,
            Progress = 0.1,
            QueuePosition = 2,
        };

        this.torrentRepository.All().Returns(new List<CoreTorrent> { torrent, nextInQueue });

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 1,
            minimumActiveCooldown: TimeSpan.Zero);

        await queueManager.ProcessQueueAsync();

        // Stalled torrent is ignored, allowing candidate torrent to be promoted
        torrent.Status.Should().Be(TorrentStatus.Downloading); // Ignored download can stay running or demoted depending on concurrency
        nextInQueue.Status.Should().Be(TorrentStatus.Downloading);
        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
    }

    [Test]
    public async Task QueueManager_DemotesExcessDownloadingTorrents_WhenConcurrencyLimitExceeded()
    {
        this.configService.MaxActiveDownloads.Returns(2);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "T1", Status = TorrentStatus.Downloading, Progress = 0.5, QueuePosition = 1 },
            new() { Id = 2, Name = "T2", Status = TorrentStatus.Downloading, Progress = 0.3, QueuePosition = 2 },
            new() { Id = 3, Name = "T3", Status = TorrentStatus.Downloading, Progress = 0.1, QueuePosition = 3 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 1,
            minimumActiveCooldown: TimeSpan.Zero);

        await queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        torrents[2].Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).PauseTorrentAsync(3);
        this.torrentRepository.Received(1).Update(Arg.Is<CoreTorrent>(t => t.Id == 3 && t.Status == TorrentStatus.Queued));
    }

    [Test]
    public async Task QueueManager_DemotesMagnetTimeoutTorrent_EvenWhenCooldownWindowIsActive()
    {
        this.configService.MagnetMetadataTimeoutSeconds.Returns(30);
        this.configService.MaxActiveDownloads.Returns(1);

        var torrent = new CoreTorrent
        {
            Id = 1,
            Name = "MagnetStuckInMetadata",
            Status = TorrentStatus.Downloading,
            TotalSize = 0,
            PieceCount = 0,
            Progress = 0,
            DownloadSpeed = 0,
            DateAdded = DateTime.UtcNow.AddSeconds(-60), // Exceeds 30s timeout
            QueuePosition = 1,
        };

        this.torrentRepository.All().Returns(new List<CoreTorrent> { torrent });

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 1,
            minimumActiveCooldown: TimeSpan.FromMinutes(10)); // Cooldown is long, but magnet timeout bypasses cooldown!

        await queueManager.ProcessQueueAsync();

        torrent.Status.Should().Be(TorrentStatus.Queued);
        await this.downloadEngine.Received(1).PauseTorrentAsync(1);
    }

    #endregion

    #region Max Active Torrent Limits Enforcement

    [Test]
    public async Task QueueManager_EnforcesMaxActiveTorrentsTotalLimit()
    {
        this.configService.MaxActiveTorrents.Returns(2);
        this.configService.MaxActiveDownloads.Returns(5);
        this.configService.MaxActiveUploads.Returns(5);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "Download 1", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 1 },
            new() { Id = 2, Name = "Download 2", Status = TorrentStatus.Downloading, Progress = 0.4, QueuePosition = 2 },
            new() { Id = 3, Name = "Seeding 1", Status = TorrentStatus.Seeding, Progress = 1.0, QueuePosition = 3 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 1,
            minimumActiveCooldown: TimeSpan.Zero);

        await queueManager.ProcessQueueAsync();

        // 2 downloads take all 2 slots of MaxActiveTorrents=2, so Seeding 1 must be demoted
        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);
        torrents[2].Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).PauseTorrentAsync(3);
    }

    [Test]
    public async Task QueueManager_UsesDownloadQueueSizeAndSeedQueueSize_WhenSet()
    {
        this.configService.DownloadQueueSize.Returns(1); // Overrides MaxActiveDownloads=5
        this.configService.MaxActiveDownloads.Returns(5);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "DL 1", Status = TorrentStatus.Downloading, Progress = 0.5, QueuePosition = 1 },
            new() { Id = 2, Name = "DL 2", Status = TorrentStatus.Downloading, Progress = 0.3, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 1,
            minimumActiveCooldown: TimeSpan.Zero);

        await queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).PauseTorrentAsync(2);
    }

    [Test]
    public async Task QueueManager_ForceStartTorrents_BypassAllLimitsAndAreNeverDemoted()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "Standard", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 1 },
            new() { Id = 2, Name = "Forced", Status = TorrentStatus.Downloading, Progress = 0.3, ForceStart = true, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 1,
            minimumActiveCooldown: TimeSpan.Zero);

        await queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Downloading);
        torrents[1].Status.Should().Be(TorrentStatus.Downloading);

        await this.downloadEngine.DidNotReceive().PauseTorrentAsync(Arg.Any<int>());
    }

    [Test]
    public async Task QueueManager_TorrentsWithNegativePriority_AreNeverAutoPromotedFromQueued()
    {
        this.configService.MaxActiveDownloads.Returns(5);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "IgnoredTorrent", Status = TorrentStatus.Queued, Priority = -1, QueuePosition = 1 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator);

        await queueManager.ProcessQueueAsync();

        torrents[0].Status.Should().Be(TorrentStatus.Queued);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(1);
    }

    #endregion

    #region Priority Promotion

    [Test]
    public async Task QueueManager_PromotesHighestPriorityQueuedTorrentFirst()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "NormalPriorityEarlyQueue", Status = TorrentStatus.Queued, Priority = 0, QueuePosition = 1 },
            new() { Id = 2, Name = "HighPriorityLateQueue", Status = TorrentStatus.Queued, Priority = 10, QueuePosition = 10 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator);

        await queueManager.ProcessQueueAsync();

        torrents.First(t => t.Id == 2).Status.Should().Be(TorrentStatus.Downloading);
        torrents.First(t => t.Id == 1).Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(1);
    }

    [Test]
    public async Task QueueManager_WhenPrioritiesEqual_PromotesLowestQueuePositionFirst()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 10, Name = "FirstInQueue", Status = TorrentStatus.Queued, Priority = 0, QueuePosition = 1 },
            new() { Id = 20, Name = "SecondInQueue", Status = TorrentStatus.Queued, Priority = 0, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator);

        await queueManager.ProcessQueueAsync();

        torrents.First(t => t.Id == 10).Status.Should().Be(TorrentStatus.Downloading);
        torrents.First(t => t.Id == 20).Status.Should().Be(TorrentStatus.Queued);

        await this.downloadEngine.Received(1).ResumeTorrentAsync(10);
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(20);
    }

    [Test]
    public async Task QueueManager_PublishesTorrentStatusChangedEvent_OnPromotion()
    {
        this.configService.MaxActiveDownloads.Returns(1);

        var torrent = new CoreTorrent
        {
            Id = 55,
            Name = "PromotedTorrent",
            Status = TorrentStatus.Queued,
            Priority = 0,
            QueuePosition = 1,
        };

        this.torrentRepository.All().Returns(new List<CoreTorrent> { torrent });

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator);

        await queueManager.ProcessQueueAsync();

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e =>
            e.Torrent.Id == 55 &&
            e.OldStatus == TorrentStatus.Queued &&
            e.NewStatus == TorrentStatus.Downloading &&
            e.IsQueueManagerInternal == true));
    }

    #endregion

    #region Slow Torrent Detection Rules

    [Test]
    public async Task QueueManager_SlowTorrentIgnored_AfterRequiredConsecutiveTicks()
    {
        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(100L); // 100 KB/s threshold
        this.configService.MaxActiveDownloads.Returns(1);

        var slowTask = Substitute.For<IDownloadTask>();
        slowTask.DownloadSpeed.Returns(50L * 1024L); // 50 KB/s (below 100 KB/s threshold)
        this.downloadEngine.GetTask(1).Returns(slowTask);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "SlowTorrent", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 1 },
            new() { Id = 2, Name = "QueuedCandidate", Status = TorrentStatus.Queued, Progress = 0.0, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 3);

        // Tick 1
        await queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Queued, "Tick 1: slow torrent not yet ignored (needs 3 ticks)");

        // Tick 2
        await queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Queued, "Tick 2: slow torrent not yet ignored");

        // Tick 3: reaches 3 consecutive slow ticks -> becomes ignored download -> candidate gets promoted!
        await queueManager.ProcessQueueAsync();
        torrents[1].Status.Should().Be(TorrentStatus.Downloading, "Tick 3: slot freed by slow torrent hysteresis");

        await this.downloadEngine.Received(1).ResumeTorrentAsync(2);
    }

    [Test]
    public async Task QueueManager_SlowTorrentTicksReset_WhenSpeedRisesAboveThreshold()
    {
        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentDownloadRateThreshold.Returns(100L); // 100 KB/s threshold
        this.configService.MaxActiveDownloads.Returns(1);

        var task = Substitute.For<IDownloadTask>();
        task.DownloadSpeed.Returns(50L * 1024L); // Slow on ticks 1 & 2
        this.downloadEngine.GetTask(1).Returns(task);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 1, Name = "FluctuatingTorrent", Status = TorrentStatus.Downloading, Progress = 0.2, QueuePosition = 1 },
            new() { Id = 2, Name = "QueuedCandidate", Status = TorrentStatus.Queued, Progress = 0.0, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 3);

        await queueManager.ProcessQueueAsync(); // Tick 1 (slow)
        await queueManager.ProcessQueueAsync(); // Tick 2 (slow)

        // Speed surges above threshold on tick 3
        task.DownloadSpeed.Returns(500L * 1024L); // 500 KB/s > 100 KB/s
        await queueManager.ProcessQueueAsync(); // Tick 3 (fast, ticks reset to 0)

        torrents[1].Status.Should().Be(TorrentStatus.Queued, "Surge in speed resets slow ticks, candidate should not be promoted");
        await this.downloadEngine.DidNotReceive().ResumeTorrentAsync(2);
    }

    [Test]
    public async Task QueueManager_SlowSeedingTorrentIgnored_AfterRequiredConsecutiveTicks()
    {
        this.configService.IgnoreSlowTorrents.Returns(true);
        this.configService.SlowTorrentUploadRateThreshold.Returns(50L); // 50 KB/s
        this.configService.MaxActiveUploads.Returns(1);

        var slowSeeder = Substitute.For<IDownloadTask>();
        slowSeeder.UploadSpeed.Returns(10L * 1024L); // 10 KB/s < 50 KB/s
        this.downloadEngine.GetTask(10).Returns(slowSeeder);

        var torrents = new List<CoreTorrent>
        {
            new() { Id = 10, Name = "SlowSeeder", Status = TorrentStatus.Seeding, Progress = 1.0, QueuePosition = 1 },
            new() { Id = 20, Name = "QueuedSeeder", Status = TorrentStatus.Queued, Progress = 1.0, QueuePosition = 2 },
        };

        this.torrentRepository.All().Returns(torrents);

        var queueManager = new QueueManagerService(
            this.torrentRepository,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            requiredSlowTicks: 2);

        await queueManager.ProcessQueueAsync(); // Tick 1
        torrents[1].Status.Should().Be(TorrentStatus.Queued);

        await queueManager.ProcessQueueAsync(); // Tick 2: slow ticks reaches 2
        torrents[1].Status.Should().Be(TorrentStatus.Seeding, "Slow upload reaches hysteresis limit, freeing upload slot for queued seeder");

        await this.downloadEngine.Received(1).ResumeTorrentAsync(20);
    }

    #endregion
}
