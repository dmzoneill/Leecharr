// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using CoreTorrent = NzbDrone.Core.Torrents.Torrent;

namespace Leecharr.Core.Test.Bandwidth;

[TestFixture]
public class SpeedSchedulerServiceTest
{
    private ISpeedScheduleRepository repository = null!;
    private IConfigService configService = null!;
    private SpeedSchedulerService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<ISpeedScheduleRepository>();
        this.configService = Substitute.For<IConfigService>();

        this.configService.MaxDownloadSpeedKbps.Returns(50000);
        this.configService.MaxUploadSpeedKbps.Returns(20000);

        this.service = new SpeedSchedulerService(this.repository, this.configService);
    }

    [Test]
    public void GetCurrentLimits_WhenNoActiveSchedule_ReturnsGlobalLimits()
    {
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 14, 0, 0)); // Monday 14:00

        limits.MaxDownloadSpeedKbps.Should().Be(50000);
        limits.MaxUploadSpeedKbps.Should().Be(20000);
        limits.IsThrottled.Should().BeFalse();
        limits.IsPaused.Should().BeFalse();
    }

    [Test]
    public void GetCurrentLimits_WhenScheduleMatches_ReturnsThrottledLimits()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Work Hours Throttling",
                Days = 127, // All days
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 10000,
                MaxUploadSpeed = 5000,
                IsEnabled = true,
                Priority = 10
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0)); // 12:00 (inside 09:00-17:00)

        limits.MaxDownloadSpeedKbps.Should().Be(10000);
        limits.MaxUploadSpeedKbps.Should().Be(5000);
        limits.IsThrottled.Should().BeTrue();
    }

    [Test]
    public void ResolveEffectiveDownloadLimit_FollowsHierarchy()
    {
        // 1. Torrent override takes precedence
        this.service.ResolveEffectiveDownloadLimit(torrentLimit: 8000, categoryLimit: 15000)
            .Should().Be(8000);

        // 2. Category limit takes next precedence
        this.service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 15000)
            .Should().Be(15000);

        // 3. Unlimited (0) per-torrent fallback when neither is set
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 0)
            .Should().Be(0);
    }

    [Test]
    public void GetCurrentLimits_WhenOvernightScheduleCrossesMidnight_MatchesCorrectly()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Night Throttling",
                Days = 1 << (int)DayOfWeek.Monday, // Monday only
                StartTime = "22:00:00",
                EndTime = "06:00:00",
                MaxDownloadSpeed = 2000,
                MaxUploadSpeed = 1000,
                IsEnabled = true,
                Priority = 10
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        // Monday 23:00 (inside Monday evening)
        var eveningLimits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 0, 0));
        eveningLimits.IsThrottled.Should().BeTrue();
        eveningLimits.MaxDownloadSpeedKbps.Should().Be(2000);

        // Tuesday 04:00 (inside early morning portion started on Monday)
        var morningLimits = this.service.GetCurrentLimits(new DateTime(2026, 9, 1, 4, 0, 0));
        morningLimits.IsThrottled.Should().BeTrue();
        morningLimits.MaxDownloadSpeedKbps.Should().Be(2000);

        // Tuesday 10:00 (outside schedule)
        var daytimeLimits = this.service.GetCurrentLimits(new DateTime(2026, 9, 1, 10, 0, 0));
        daytimeLimits.IsThrottled.Should().BeFalse();
    }

    [Test]
    public void GetCurrentLimits_WhenScheduleThrottlesOnlyDownload_UploadSetsUnlimitedOverride()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Download Only Throttling",
                Days = 127,
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 2000,
                MaxUploadSpeed = 0, // Explicitly unlimited by schedule
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));

        limits.MaxDownloadSpeedKbps.Should().Be(2000);
        limits.MaxUploadSpeedKbps.Should().Be(0);
        limits.IsThrottled.Should().BeTrue();
        limits.HasActiveSchedule.Should().BeTrue();

        this.service.ResolveEffectiveDownloadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(0);
        this.service.ResolveEffectiveUploadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(0);
    }

    [Test]
    public void GetCurrentLimits_WhenScheduleThrottlesOnlyUpload_DownloadSetsUnlimitedOverride()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Upload Only Throttling",
                Days = 127,
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 0, // Explicitly unlimited by schedule
                MaxUploadSpeed = 300,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));

        limits.MaxDownloadSpeedKbps.Should().Be(0);
        limits.MaxUploadSpeedKbps.Should().Be(300);
        limits.IsThrottled.Should().BeTrue();
        limits.HasActiveSchedule.Should().BeTrue();

        this.service.ResolveEffectiveDownloadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(0);
        this.service.ResolveEffectiveUploadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(0);
    }

    [Test]
    public void ResolveEffectiveDownloadLimit_WhenGlobalLimitSetAndActiveScheduleIsZero_ReturnsZeroUnlimited()
    {
        // Global limit set to restrictive daytime speed (e.g. 1000 KB/s DL, 500 KB/s UL)
        this.configService.MaxDownloadSpeedKbps.Returns(1000);
        this.configService.MaxUploadSpeedKbps.Returns(500);

        // Active off-peak schedule configured for unlimited speeds (0 KB/s)
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Overnight Unlimited",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "08:00:00",
                MaxDownloadSpeed = 0,
                MaxUploadSpeed = 0,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var checkTime = new DateTime(2026, 8, 31, 4, 0, 0); // 04:00 (inside overnight schedule)
        var limits = this.service.GetCurrentLimits(checkTime);

        limits.MaxDownloadSpeedKbps.Should().Be(0);
        limits.MaxUploadSpeedKbps.Should().Be(0);
        limits.HasActiveSchedule.Should().BeTrue();

        var effectiveDownload = this.service.ResolveEffectiveDownloadLimit(0, 0, checkTime);
        var effectiveUpload = this.service.ResolveEffectiveUploadLimit(0, 0, checkTime);

        effectiveDownload.Should().Be(0, "active schedule with 0 KB/s must override global throttled limit to unlimited");
        effectiveUpload.Should().Be(0, "active schedule with 0 KB/s must override global throttled limit to unlimited");
    }

    [Test]
    public void GetCurrentLimits_WithNonPaddedAndSecondPrecisionTimes_MatchesCorrectly()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Flexible Format Schedule",
                Days = 127,
                StartTime = "9:00",
                EndTime = "23:59:01",
                MaxDownloadSpeed = 1500,
                MaxUploadSpeed = 800,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var limitsAtStart = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 9, 0, 0));
        limitsAtStart.IsThrottled.Should().BeTrue();
        limitsAtStart.MaxDownloadSpeedKbps.Should().Be(1500);

        var limitsAtEnd = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 59, 1));
        limitsAtEnd.IsThrottled.Should().BeTrue();
        limitsAtEnd.MaxDownloadSpeedKbps.Should().Be(1500);

        var limitsBeforeStart = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 8, 59, 59));
        limitsBeforeStart.IsThrottled.Should().BeFalse();
    }

    [Test]
    public void GetCurrentLimits_WithInvalidTimeString_GracefullyIgnoresSchedule()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Invalid Schedule",
                Days = 127,
                StartTime = "invalid-time",
                EndTime = "23:59:00",
                MaxDownloadSpeed = 1500,
                MaxUploadSpeed = 800,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));
        limits.IsThrottled.Should().BeFalse();
        limits.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    [Test]
    public void GetCurrentLimits_WithMinutePrecisionEndTime_RemainsActiveThroughoutEntireMinute()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Minute Precision Schedule",
                Days = 127,
                StartTime = "09:00",
                EndTime = "23:59",
                MaxDownloadSpeed = 1500,
                MaxUploadSpeed = 800,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        // At 23:59:30, schedule configured with "23:59" must still be active
        var limitsMidMinute = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 59, 30));
        limitsMidMinute.IsThrottled.Should().BeTrue();
        limitsMidMinute.MaxDownloadSpeedKbps.Should().Be(1500);

        // At 23:59:59, schedule configured with "23:59" must still be active
        var limitsEndMinute = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 59, 59));
        limitsEndMinute.IsThrottled.Should().BeTrue();
        limitsEndMinute.MaxDownloadSpeedKbps.Should().Be(1500);
    }

    [Test]
    public void GetCurrentLimits_WhenConfigSchedulerActive_ReturnsAltSpeeds()
    {
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(9);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(17);
        this.configService.SchedulerEndMinute.Returns(0);
        this.configService.SchedulerMonday.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(5000);
        this.configService.AltUploadSpeedKbps.Returns(2500);

        // Monday 12:00:00 (inside schedule)
        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));
        limits.MaxDownloadSpeedKbps.Should().Be(5000);
        limits.MaxUploadSpeedKbps.Should().Be(2500);
        limits.IsThrottled.Should().BeTrue();
        limits.IsPaused.Should().BeFalse();

        // Monday 17:00:30 (inside minute precision end time)
        var limitsAtEndMinute = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 17, 0, 30));
        limitsAtEndMinute.IsThrottled.Should().BeTrue();
        limitsAtEndMinute.MaxDownloadSpeedKbps.Should().Be(5000);

        // Monday 08:59:59 (before schedule)
        var limitsBefore = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 8, 59, 59));
        limitsBefore.IsThrottled.Should().BeFalse();
        limitsBefore.MaxDownloadSpeedKbps.Should().Be(50000);

        // Monday 17:01:00 (after schedule)
        var limitsAfter = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 17, 1, 0));
        limitsAfter.IsThrottled.Should().BeFalse();
        limitsAfter.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    [Test]
    public void GetCurrentLimits_WhenConfigSchedulerDayDisabled_ReturnsGlobalLimits()
    {
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(9);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(17);
        this.configService.SchedulerEndMinute.Returns(0);
        this.configService.SchedulerMonday.Returns(true);
        this.configService.SchedulerTuesday.Returns(false);
        this.configService.AltDownloadSpeedKbps.Returns(5000);
        this.configService.AltUploadSpeedKbps.Returns(2500);

        // Tuesday 12:00:00 (Tuesday is disabled)
        var limits = this.service.GetCurrentLimits(new DateTime(2026, 9, 1, 12, 0, 0));
        limits.MaxDownloadSpeedKbps.Should().Be(50000);
        limits.MaxUploadSpeedKbps.Should().Be(20000);
        limits.IsThrottled.Should().BeFalse();
    }

    [Test]
    public void GetCurrentLimits_WhenConfigSchedulerOvernight_HandlesMidnightCrossing()
    {
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(22);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(6);
        this.configService.SchedulerEndMinute.Returns(0);
        this.configService.SchedulerMonday.Returns(true);
        this.configService.SchedulerTuesday.Returns(false);
        this.configService.AltDownloadSpeedKbps.Returns(3000);
        this.configService.AltUploadSpeedKbps.Returns(1500);

        // Monday 23:00 (Monday evening)
        var eveningLimits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 0, 0));
        eveningLimits.IsThrottled.Should().BeTrue();
        eveningLimits.MaxDownloadSpeedKbps.Should().Be(3000);

        // Tuesday 04:00 (Early morning started on Monday)
        var morningLimits = this.service.GetCurrentLimits(new DateTime(2026, 9, 1, 4, 0, 0));
        morningLimits.IsThrottled.Should().BeTrue();
        morningLimits.MaxDownloadSpeedKbps.Should().Be(3000);

        // Tuesday 10:00 (Outside schedule)
        var dayLimits = this.service.GetCurrentLimits(new DateTime(2026, 9, 1, 10, 0, 0));
        dayLimits.IsThrottled.Should().BeFalse();
        dayLimits.MaxDownloadSpeedKbps.Should().Be(50000);

        // Monday 04:00 (Sunday overnight was not enabled)
        this.configService.SchedulerSunday.Returns(false);
        var mondayEarlyMorning = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 4, 0, 0));
        mondayEarlyMorning.IsThrottled.Should().BeFalse();
        mondayEarlyMorning.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    [Test]
    public void GetCurrentLimits_WhenDatabaseScheduleAndConfigSchedulerBothActive_DatabaseScheduleTakesPrecedence()
    {
        var dbSchedule = new List<SpeedSchedule>
        {
            new()
            {
                Name = "DB Schedule",
                Days = 127,
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 12000,
                MaxUploadSpeed = 6000,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(dbSchedule);
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(9);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(17);
        this.configService.SchedulerEndMinute.Returns(0);
        this.configService.SchedulerMonday.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(2000);
        this.configService.AltUploadSpeedKbps.Returns(1000);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));
        limits.MaxDownloadSpeedKbps.Should().Be(12000);
        limits.MaxUploadSpeedKbps.Should().Be(6000);
        limits.IsThrottled.Should().BeTrue();
    }

    [Test]
    public void GetCurrentLimits_WhenConfigSchedulerPaused_SetsIsPausedTrueAndZeroLimits()
    {
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(9);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(17);
        this.configService.SchedulerEndMinute.Returns(0);
        this.configService.SchedulerMonday.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(-1);
        this.configService.AltUploadSpeedKbps.Returns(-1);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));
        limits.MaxDownloadSpeedKbps.Should().Be(0);
        limits.MaxUploadSpeedKbps.Should().Be(0);
        limits.IsPaused.Should().BeTrue();
        limits.IsThrottled.Should().BeFalse();
    }

    [TestCase(DayOfWeek.Sunday, 6)]
    [TestCase(DayOfWeek.Monday, 0)]
    [TestCase(DayOfWeek.Tuesday, 1)]
    [TestCase(DayOfWeek.Wednesday, 2)]
    [TestCase(DayOfWeek.Thursday, 3)]
    [TestCase(DayOfWeek.Friday, 4)]
    [TestCase(DayOfWeek.Saturday, 5)]
    public void GetCurrentLimits_EvaluatesEachDayFlagCorrectly(DayOfWeek dayOfWeek, int dayOffset)
    {
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(9);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(17);
        this.configService.SchedulerEndMinute.Returns(0);
        this.configService.AltDownloadSpeedKbps.Returns(4000);
        this.configService.AltUploadSpeedKbps.Returns(2000);

        this.configService.SchedulerSunday.Returns(dayOfWeek == DayOfWeek.Sunday);
        this.configService.SchedulerMonday.Returns(dayOfWeek == DayOfWeek.Monday);
        this.configService.SchedulerTuesday.Returns(dayOfWeek == DayOfWeek.Tuesday);
        this.configService.SchedulerWednesday.Returns(dayOfWeek == DayOfWeek.Wednesday);
        this.configService.SchedulerThursday.Returns(dayOfWeek == DayOfWeek.Thursday);
        this.configService.SchedulerFriday.Returns(dayOfWeek == DayOfWeek.Friday);
        this.configService.SchedulerSaturday.Returns(dayOfWeek == DayOfWeek.Saturday);

        var date = new DateTime(2026, 8, 31, 12, 0, 0).AddDays(dayOffset);
        date.DayOfWeek.Should().Be(dayOfWeek);

        var limits = this.service.GetCurrentLimits(date);
        limits.IsThrottled.Should().BeTrue();
        limits.MaxDownloadSpeedKbps.Should().Be(4000);
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_WhenPaused_InvokesPauseAllAsyncOnEngine()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var schedulerService = new SpeedSchedulerService(this.repository, this.configService, downloadEngine);

        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Pause Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = -1,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        await schedulerService.ApplyCurrentLimitsAsync();

        await downloadEngine.Received(1).PauseAllAsync();
        await downloadEngine.DidNotReceive().SetRateLimitsAsync(Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_WhenOnlyUploadPaused_SetsUploadTo1AndDownloadToUnthrottled()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var schedulerService = new SpeedSchedulerService(this.repository, this.configService, downloadEngine);

        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Upload Pause Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = 0,
                MaxUploadSpeed = -1,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        await schedulerService.ApplyCurrentLimitsAsync();

        await downloadEngine.DidNotReceive().PauseAllAsync();
        await downloadEngine.Received(1).SetRateLimitsAsync(0, 1);
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_WhenOnlyDownloadPaused_SetsDownloadTo1AndUploadToUnthrottled()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var schedulerService = new SpeedSchedulerService(this.repository, this.configService, downloadEngine);

        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Download Pause Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = 0,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        await schedulerService.ApplyCurrentLimitsAsync();

        await downloadEngine.DidNotReceive().PauseAllAsync();
        await downloadEngine.Received(1).SetRateLimitsAsync(1, 0);
    }

    [Test]
    public void GetCurrentLimits_WhenAsymmetricSchedule_SetsCorrectDirectionalPauseFlags()
    {
        var uploadPausedSchedule = new SpeedSchedule
        {
            Name = "Upload Pause",
            Days = 127,
            StartTime = "00:00:00",
            EndTime = "23:59:59",
            MaxDownloadSpeed = 5000,
            MaxUploadSpeed = -1,
            IsEnabled = true,
            Priority = 10,
        };

        this.repository.GetEnabled().Returns(new List<SpeedSchedule> { uploadPausedSchedule });

        var limits = this.service.GetCurrentLimits();

        limits.IsPaused.Should().BeTrue();
        limits.IsDownloadPaused.Should().BeFalse();
        limits.IsUploadPaused.Should().BeTrue();
        limits.MaxDownloadSpeedKbps.Should().Be(5000);
        limits.MaxUploadSpeedKbps.Should().Be(0);
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_WhenPauseScheduleElapses_ResumesSchedulerPausedTorrentsAndAppliesNormalLimits()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var activeTask = Substitute.For<IDownloadTask>();
        activeTask.TorrentId.Returns(42);
        activeTask.Status.Returns(TorrentStatus.Downloading);
        downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { activeTask });
        downloadEngine.GetTask(42).Returns(activeTask);

        var schedulerService = new SpeedSchedulerService(this.repository, this.configService, downloadEngine);

        var pauseSchedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Pause Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = -1,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(pauseSchedules);
        await schedulerService.ApplyCurrentLimitsAsync();
        await downloadEngine.Received(1).PauseAllAsync();
        schedulerService.SchedulerPausedTorrentIds.Should().Contain(42);

        // Window elapses -> no active schedule
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        await schedulerService.ApplyCurrentLimitsAsync();

        await downloadEngine.Received(1).ResumeTorrentAsync(42);
        await downloadEngine.DidNotReceive().ResumeAllTorrentsAsync();
        await downloadEngine.Received(1).SetRateLimitsAsync(50000, 20000);
        schedulerService.SchedulerPausedTorrentIds.Should().BeEmpty();
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_WhenPauseScheduleElapses_DoesNotResumeUserPausedTorrents()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var torrentRepo = Substitute.For<ITorrentRepository>();

        var task1 = Substitute.For<IDownloadTask>();
        task1.TorrentId.Returns(1);
        task1.Status.Returns(TorrentStatus.Downloading);

        var task2 = Substitute.For<IDownloadTask>();
        task2.TorrentId.Returns(2);
        task2.Status.Returns(TorrentStatus.Paused);

        downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { task1, task2 });
        downloadEngine.GetTask(1).Returns(task1);
        downloadEngine.GetTask(2).Returns(task2);

        var dbTorrent1 = new CoreTorrent { Id = 1, Status = TorrentStatus.Downloading };
        var dbTorrent2 = new CoreTorrent { Id = 2, Status = TorrentStatus.Paused };
        torrentRepo.All().Returns(new List<CoreTorrent> { dbTorrent1, dbTorrent2 });
        torrentRepo.Get(1).Returns(dbTorrent1);
        torrentRepo.Get(2).Returns(dbTorrent2);

        var schedulerService = new SpeedSchedulerService(
            this.repository,
            this.configService,
            downloadEngine,
            torrentRepository: torrentRepo);

        var pauseSchedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Pause Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = -1,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(pauseSchedules);
        await schedulerService.ApplyCurrentLimitsAsync();

        // Torrent 1 was active, torrent 2 was paused
        schedulerService.SchedulerPausedTorrentIds.Should().Contain(1);
        schedulerService.SchedulerPausedTorrentIds.Should().NotContain(2);

        // While scheduler pause is active, user pauses torrent 1
        dbTorrent1.Status = TorrentStatus.Paused;

        // Schedule elapses
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        await schedulerService.ApplyCurrentLimitsAsync();

        // Neither torrent should be resumed
        await downloadEngine.DidNotReceive().ResumeTorrentAsync(1);
        await downloadEngine.DidNotReceive().ResumeTorrentAsync(2);
        await downloadEngine.DidNotReceive().ResumeAllTorrentsAsync();
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_WhenPauseScheduleElapses_DoesNotResumeStoppedOrCompletedTorrents()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var torrentRepo = Substitute.For<ITorrentRepository>();

        var task1 = Substitute.For<IDownloadTask>();
        task1.TorrentId.Returns(10);
        task1.Status.Returns(TorrentStatus.Downloading);

        var task2 = Substitute.For<IDownloadTask>();
        task2.TorrentId.Returns(20);
        task2.Status.Returns(TorrentStatus.Completed);

        downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { task1, task2 });
        downloadEngine.GetTask(10).Returns(task1);
        downloadEngine.GetTask(20).Returns(task2);

        var dbTorrent1 = new CoreTorrent { Id = 10, Status = TorrentStatus.Downloading };
        var dbTorrent2 = new CoreTorrent { Id = 20, Status = TorrentStatus.Completed };
        torrentRepo.All().Returns(new List<CoreTorrent> { dbTorrent1, dbTorrent2 });
        torrentRepo.Get(10).Returns(dbTorrent1);
        torrentRepo.Get(20).Returns(dbTorrent2);

        var schedulerService = new SpeedSchedulerService(
            this.repository,
            this.configService,
            downloadEngine,
            torrentRepository: torrentRepo);

        var pauseSchedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Pause Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = -1,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(pauseSchedules);
        await schedulerService.ApplyCurrentLimitsAsync();

        // Torrent 10 tracked, 20 skipped
        schedulerService.SchedulerPausedTorrentIds.Should().Contain(10);
        schedulerService.SchedulerPausedTorrentIds.Should().NotContain(20);

        // In the interim, seed goals completed for torrent 10
        dbTorrent1.Status = TorrentStatus.Completed;

        // Schedule elapses
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        await schedulerService.ApplyCurrentLimitsAsync();

        await downloadEngine.DidNotReceive().ResumeTorrentAsync(10);
        await downloadEngine.DidNotReceive().ResumeTorrentAsync(20);
        await downloadEngine.DidNotReceive().ResumeAllTorrentsAsync();
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_WhenPauseScheduleElapses_InvokesQueueManagerProcessQueueAsync()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var queueManagerService = Substitute.For<IQueueManagerService>();

        var task = Substitute.For<IDownloadTask>();
        task.TorrentId.Returns(100);
        task.Status.Returns(TorrentStatus.Downloading);
        downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { task });
        downloadEngine.GetTask(100).Returns(task);

        var schedulerService = new SpeedSchedulerService(
            this.repository,
            this.configService,
            downloadEngine,
            queueManagerService: queueManagerService);

        var pauseSchedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Pause Schedule",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = -1,
                MaxUploadSpeed = -1,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(pauseSchedules);
        await schedulerService.ApplyCurrentLimitsAsync();

        // Window elapses
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        await schedulerService.ApplyCurrentLimitsAsync();

        await downloadEngine.Received(1).ResumeTorrentAsync(100);
        await queueManagerService.Received(1).ProcessQueueAsync();
    }

    [Test]
    public void GetCurrentLimits_WithConfiguredTimeZone_ConvertsUtcTimeToLocalTimeZoneCorrectly()
    {
        // America/New_York is UTC-4 in August (EDT)
        this.configService.TimeZone.Returns("America/New_York");

        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Morning Throttling NY",
                Days = 1 << (int)DayOfWeek.Monday, // Monday only
                StartTime = "08:00:00",
                EndTime = "12:00:00",
                MaxDownloadSpeed = 3000,
                MaxUploadSpeed = 1500,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        // Monday 13:00 UTC == Monday 09:00 EDT in New York (inside 08:00-12:00 window)
        var utcInside = new DateTime(2026, 8, 31, 13, 0, 0, DateTimeKind.Utc);
        var limitsInside = this.service.GetCurrentLimits(utcInside);
        limitsInside.IsThrottled.Should().BeTrue();
        limitsInside.MaxDownloadSpeedKbps.Should().Be(3000);

        // Monday 17:00 UTC == Monday 13:00 EDT in New York (outside 08:00-12:00 window)
        var utcOutside = new DateTime(2026, 8, 31, 17, 0, 0, DateTimeKind.Utc);
        var limitsOutside = this.service.GetCurrentLimits(utcOutside);
        limitsOutside.IsThrottled.Should().BeFalse();
        limitsOutside.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    [Test]
    public void GetCurrentLimits_WithConfiguredTimeZone_OvernightAcrossDaysEvaluatedInLocalTime()
    {
        // Asia/Tokyo is UTC+9
        this.configService.TimeZone.Returns("Asia/Tokyo");

        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Tuesday Morning Tokyo",
                Days = 1 << (int)DayOfWeek.Tuesday, // Tuesday only in Tokyo
                StartTime = "01:00:00",
                EndTime = "05:00:00",
                MaxDownloadSpeed = 2500,
                MaxUploadSpeed = 1000,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        // Monday 17:00 UTC == Tuesday 02:00 Tokyo time (inside Tuesday 01:00-05:00)
        var utcDate = new DateTime(2026, 8, 31, 17, 0, 0, DateTimeKind.Utc);
        var limits = this.service.GetCurrentLimits(utcDate);

        limits.IsThrottled.Should().BeTrue();
        limits.MaxDownloadSpeedKbps.Should().Be(2500);
    }

    [Test]
    public void GetCurrentLimits_WithSecondPrecisionEndTimeAt235959_RemainsActiveDuringFinalSubSecond()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "End of Day Throttling",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = 1500,
                MaxUploadSpeed = 750,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        // Sub-second timestamps in final second before midnight
        var limitsAt500Ms = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 59, 59, 500));
        limitsAt500Ms.IsThrottled.Should().BeTrue();
        limitsAt500Ms.MaxDownloadSpeedKbps.Should().Be(1500);

        var limitsAt999Ms = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 59, 59, 999));
        limitsAt999Ms.IsThrottled.Should().BeTrue();
        limitsAt999Ms.MaxDownloadSpeedKbps.Should().Be(1500);
    }

    [Test]
    public void GetCurrentLimits_WithConfigSchedulerAt2359_RemainsActiveDuringFinalSubSecond()
    {
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.configService.SchedulerEnabled.Returns(true);
        this.configService.SchedulerStartHour.Returns(0);
        this.configService.SchedulerStartMinute.Returns(0);
        this.configService.SchedulerEndHour.Returns(23);
        this.configService.SchedulerEndMinute.Returns(59);
        this.configService.SchedulerMonday.Returns(true);
        this.configService.AltDownloadSpeedKbps.Returns(2200);
        this.configService.AltUploadSpeedKbps.Returns(1100);

        var limitsAt999Ms = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 59, 59, 999));
        limitsAt999Ms.IsThrottled.Should().BeTrue();
        limitsAt999Ms.MaxDownloadSpeedKbps.Should().Be(2200);
    }

    [Test]
    public void GetCurrentLimits_WhenUtcTimePassedWithoutConfiguredTimeZone_NormalizesToLocalTime()
    {
        this.configService.TimeZone.Returns((string)null!);

        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Local Day Throttling",
                Days = 127,
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 3300,
                MaxUploadSpeed = 1200,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        // Construct a local time that is inside the schedule, then convert to UTC
        var localInside = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Local);
        var utcInside = localInside.ToUniversalTime();

        var limits = this.service.GetCurrentLimits(utcInside);
        limits.IsThrottled.Should().BeTrue();
        limits.MaxDownloadSpeedKbps.Should().Be(3300);
    }
}
