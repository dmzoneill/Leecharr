// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Configuration;

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
    public void ResolveEffectiveDownloadLimit_Follows4LevelHierarchy()
    {
        // 1. Torrent override takes precedence
        this.service.ResolveEffectiveDownloadLimit(torrentLimit: 8000, categoryLimit: 15000)
            .Should().Be(8000);

        // 2. Category limit takes next precedence
        this.service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 15000)
            .Should().Be(15000);

        // 3. Global limit fallback
        this.repository.GetEnabled().Returns(new List<SpeedSchedule>());
        this.service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 0)
            .Should().Be(50000);
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
    public void GetCurrentLimits_WhenScheduleThrottlesOnlyDownload_UploadFallsBackToGlobalLimit()
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
                MaxUploadSpeed = 0, // Unconstrained by schedule
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));

        limits.MaxDownloadSpeedKbps.Should().Be(2000);
        limits.MaxUploadSpeedKbps.Should().Be(20000); // Falls back to configService.MaxUploadSpeedKbps
        limits.IsThrottled.Should().BeTrue();

        this.service.ResolveEffectiveDownloadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(2000);
        this.service.ResolveEffectiveUploadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(20000);
    }

    [Test]
    public void GetCurrentLimits_WhenScheduleThrottlesOnlyUpload_DownloadFallsBackToGlobalLimit()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Upload Only Throttling",
                Days = 127,
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 0, // Unconstrained by schedule
                MaxUploadSpeed = 300,
                IsEnabled = true,
                Priority = 10,
            },
        };

        this.repository.GetEnabled().Returns(schedules);

        var limits = this.service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0));

        limits.MaxDownloadSpeedKbps.Should().Be(50000); // Falls back to configService.MaxDownloadSpeedKbps
        limits.MaxUploadSpeedKbps.Should().Be(300);
        limits.IsThrottled.Should().BeTrue();

        this.service.ResolveEffectiveDownloadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(50000);
        this.service.ResolveEffectiveUploadLimit(0, 0, new DateTime(2026, 8, 31, 12, 0, 0))
            .Should().Be(300);
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
}
