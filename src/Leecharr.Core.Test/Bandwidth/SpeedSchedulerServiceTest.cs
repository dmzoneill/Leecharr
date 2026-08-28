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
    private ISpeedScheduleRepository _repository = null!;
    private IConfigService _configService = null!;
    private SpeedSchedulerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ISpeedScheduleRepository>();
        _configService = Substitute.For<IConfigService>();

        _configService.MaxDownloadSpeedKbps.Returns(50000);
        _configService.MaxUploadSpeedKbps.Returns(20000);

        _service = new SpeedSchedulerService(_repository, _configService);
    }

    [Test]
    public void GetCurrentLimits_WhenNoActiveSchedule_ReturnsGlobalLimits()
    {
        _repository.GetEnabled().Returns(new List<SpeedSchedule>());

        var limits = _service.GetCurrentLimits(new DateTime(2026, 8, 31, 14, 0, 0)); // Monday 14:00

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
            }
        };

        _repository.GetEnabled().Returns(schedules);

        var limits = _service.GetCurrentLimits(new DateTime(2026, 8, 31, 12, 0, 0)); // 12:00 (inside 09:00-17:00)

        limits.MaxDownloadSpeedKbps.Should().Be(10000);
        limits.MaxUploadSpeedKbps.Should().Be(5000);
        limits.IsThrottled.Should().BeTrue();
    }

    [Test]
    public void ResolveEffectiveDownloadLimit_Follows4LevelHierarchy()
    {
        // 1. Torrent override takes precedence
        _service.ResolveEffectiveDownloadLimit(torrentLimit: 8000, categoryLimit: 15000)
            .Should().Be(8000);

        // 2. Category limit takes next precedence
        _service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 15000)
            .Should().Be(15000);

        // 3. Global limit fallback
        _repository.GetEnabled().Returns(new List<SpeedSchedule>());
        _service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 0)
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
            }
        };

        _repository.GetEnabled().Returns(schedules);

        // Monday 23:00 (inside Monday evening)
        var eveningLimits = _service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 0, 0));
        eveningLimits.IsThrottled.Should().BeTrue();
        eveningLimits.MaxDownloadSpeedKbps.Should().Be(2000);

        // Tuesday 04:00 (inside early morning portion started on Monday)
        var morningLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 1, 4, 0, 0));
        morningLimits.IsThrottled.Should().BeTrue();
        morningLimits.MaxDownloadSpeedKbps.Should().Be(2000);

        // Tuesday 10:00 (outside schedule)
        var daytimeLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 1, 10, 0, 0));
        daytimeLimits.IsThrottled.Should().BeFalse();
    }
}
