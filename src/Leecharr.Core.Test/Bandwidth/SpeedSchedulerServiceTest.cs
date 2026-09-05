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
}
