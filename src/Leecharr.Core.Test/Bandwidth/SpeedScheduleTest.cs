using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Bandwidth;

[TestFixture]
public class SpeedScheduleTest
{
    private ISpeedScheduleRepository _repository = null!;
    private IConfigService _configService = null!;
    private IDownloadEngine _downloadEngine = null!;
    private SpeedSchedulerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ISpeedScheduleRepository>();
        _configService = Substitute.For<IConfigService>();
        _downloadEngine = Substitute.For<IDownloadEngine>();

        _configService.MaxDownloadSpeedKbps.Returns(50000);
        _configService.MaxUploadSpeedKbps.Returns(20000);

        _service = new SpeedSchedulerService(_repository, _configService, _downloadEngine);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    #region 24x7 Matrix & Day Bitmask Tests

    [Test]
    public void DayOfWeek_BitmaskAlignment_MatchesFrontendAndBackendFlags()
    {
        // Assert DayOfWeek bitmask values: 1 << (int)DayOfWeek
        (1 << (int)DayOfWeek.Sunday).Should().Be(1);
        (1 << (int)DayOfWeek.Monday).Should().Be(2);
        (1 << (int)DayOfWeek.Tuesday).Should().Be(4);
        (1 << (int)DayOfWeek.Wednesday).Should().Be(8);
        (1 << (int)DayOfWeek.Thursday).Should().Be(16);
        (1 << (int)DayOfWeek.Friday).Should().Be(32);
        (1 << (int)DayOfWeek.Saturday).Should().Be(64);

        // Presets
        var weekdays = (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Tuesday) | (1 << (int)DayOfWeek.Wednesday) | (1 << (int)DayOfWeek.Thursday) | (1 << (int)DayOfWeek.Friday);
        weekdays.Should().Be(62);

        var weekends = (1 << (int)DayOfWeek.Saturday) | (1 << (int)DayOfWeek.Sunday);
        weekends.Should().Be(65);

        var everyday = weekdays | weekends;
        everyday.Should().Be(127);
    }

    [Test]
    public void GetCurrentLimits_WeekdaySchedule_MatchesOnlyMondayThroughFriday()
    {
        // Weekdays bitmask: Mon(2) + Tue(4) + Wed(8) + Thu(16) + Fri(32) = 62
        var schedule = new SpeedSchedule
        {
            Name = "Weekday Work Hours",
            Days = 62,
            StartTime = "09:00:00",
            EndTime = "17:00:00",
            MaxDownloadSpeed = 8000,
            MaxUploadSpeed = 2000,
            IsEnabled = true,
            Priority = 10
        };

        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        // Monday 11:00 (inside weekday window) -> Throttled
        var mondayLimits = _service.GetCurrentLimits(new DateTime(2026, 8, 31, 11, 0, 0)); // Mon
        mondayLimits.IsThrottled.Should().BeTrue();
        mondayLimits.MaxDownloadSpeedKbps.Should().Be(8000);

        // Wednesday 14:00 (inside weekday window) -> Throttled
        var wednesdayLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 2, 14, 0, 0)); // Wed
        wednesdayLimits.IsThrottled.Should().BeTrue();
        wednesdayLimits.MaxDownloadSpeedKbps.Should().Be(8000);

        // Saturday 11:00 (weekend, outside weekday window) -> Unthrottled
        var saturdayLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 5, 11, 0, 0)); // Sat
        saturdayLimits.IsThrottled.Should().BeFalse();
        saturdayLimits.MaxDownloadSpeedKbps.Should().Be(50000);

        // Sunday 14:00 (weekend, outside weekday window) -> Unthrottled
        var sundayLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 6, 14, 0, 0)); // Sun
        sundayLimits.IsThrottled.Should().BeFalse();
        sundayLimits.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    [Test]
    public void GetCurrentLimits_WeekendSchedule_MatchesSaturdayAndSundayOnly()
    {
        // Weekend bitmask: Sun(1) + Sat(64) = 65
        var schedule = new SpeedSchedule
        {
            Name = "Weekend Boost",
            Days = 65,
            StartTime = "00:00:00",
            EndTime = "23:59:59",
            MaxDownloadSpeed = 100000,
            MaxUploadSpeed = 50000,
            IsEnabled = true,
            Priority = 5
        };

        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        // Saturday 12:00 -> Matches
        var satLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 5, 12, 0, 0));
        satLimits.MaxDownloadSpeedKbps.Should().Be(100000);

        // Sunday 12:00 -> Matches
        var sunLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 6, 12, 0, 0));
        sunLimits.MaxDownloadSpeedKbps.Should().Be(100000);

        // Friday 12:00 -> Does not match
        var friLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 4, 12, 0, 0));
        friLimits.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    #endregion

    #region Overnight Schedule Tests (Cross-Midnight)

    [Test]
    public void GetCurrentLimits_OvernightSchedule_HandlesMidnightBoundaryTransition()
    {
        // Overnight schedule: Monday 22:00 to 06:00
        var schedule = new SpeedSchedule
        {
            Name = "Monday Night Owls",
            Days = 1 << (int)DayOfWeek.Monday, // Monday only (2)
            StartTime = "22:00:00",
            EndTime = "06:00:00",
            MaxDownloadSpeed = 1500,
            MaxUploadSpeed = 500,
            IsEnabled = true,
            Priority = 10
        };

        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        // 1. Monday 21:59:59 (before overnight window) -> Unthrottled
        var beforeLimits = _service.GetCurrentLimits(new DateTime(2026, 8, 31, 21, 59, 59));
        beforeLimits.IsThrottled.Should().BeFalse();

        // 2. Monday 22:00:00 (exact start) -> Throttled
        var startLimits = _service.GetCurrentLimits(new DateTime(2026, 8, 31, 22, 0, 0));
        startLimits.IsThrottled.Should().BeTrue();
        startLimits.MaxDownloadSpeedKbps.Should().Be(1500);

        // 3. Monday 23:30:00 (evening portion) -> Throttled
        var eveningLimits = _service.GetCurrentLimits(new DateTime(2026, 8, 31, 23, 30, 0));
        eveningLimits.IsThrottled.Should().BeTrue();
        eveningLimits.MaxDownloadSpeedKbps.Should().Be(1500);

        // 4. Tuesday 03:00:00 (early morning continuation of Monday night) -> Throttled
        var morningLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 1, 3, 0, 0));
        morningLimits.IsThrottled.Should().BeTrue();
        morningLimits.MaxDownloadSpeedKbps.Should().Be(1500);

        // 5. Tuesday 06:00:00 (exact end of overnight window) -> Throttled
        var endLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 1, 6, 0, 0));
        endLimits.IsThrottled.Should().BeTrue();

        // 6. Tuesday 06:00:01 (after overnight window) -> Unthrottled
        var daytimeLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 1, 6, 0, 1));
        daytimeLimits.IsThrottled.Should().BeFalse();
        daytimeLimits.MaxDownloadSpeedKbps.Should().Be(50000);
    }

    #endregion

    #region Priority Ordering & Overlapping Schedules

    [Test]
    public void GetCurrentLimits_OverlappingSchedules_HighestPriorityWins()
    {
        var lowPrioritySchedule = new SpeedSchedule
        {
            Name = "General Daytime",
            Days = 127,
            StartTime = "08:00:00",
            EndTime = "20:00:00",
            MaxDownloadSpeed = 10000,
            MaxUploadSpeed = 5000,
            IsEnabled = true,
            Priority = 5
        };

        var highPrioritySchedule = new SpeedSchedule
        {
            Name = "Intensive Meeting Hours",
            Days = 127,
            StartTime = "13:00:00",
            EndTime = "15:00:00",
            MaxDownloadSpeed = 1000,
            MaxUploadSpeed = 500,
            IsEnabled = true,
            Priority = 20
        };

        _repository.GetEnabled().Returns(new List<SpeedSchedule> { lowPrioritySchedule, highPrioritySchedule });

        // At 14:00, both schedules match. The high priority schedule (Priority 20) should win.
        var limits = _service.GetCurrentLimits(new DateTime(2026, 9, 2, 14, 0, 0));

        limits.MaxDownloadSpeedKbps.Should().Be(1000);
        limits.MaxUploadSpeedKbps.Should().Be(500);

        // At 10:00, only the low priority schedule matches.
        var morningLimits = _service.GetCurrentLimits(new DateTime(2026, 9, 2, 10, 0, 0));
        morningLimits.MaxDownloadSpeedKbps.Should().Be(10000);
    }

    #endregion

    #region Paused Schedules & Application Tests

    [Test]
    public void GetCurrentLimits_PausedSchedule_SetsIsPausedTrueAndZeroRate()
    {
        var pausedSchedule = new SpeedSchedule
        {
            Name = "Business Hours Pause",
            Days = 127,
            StartTime = "09:00:00",
            EndTime = "17:00:00",
            MaxDownloadSpeed = -1,
            MaxUploadSpeed = -1,
            IsEnabled = true,
            Priority = 10
        };

        _repository.GetEnabled().Returns(new List<SpeedSchedule> { pausedSchedule });

        var limits = _service.GetCurrentLimits(new DateTime(2026, 9, 2, 12, 0, 0));

        limits.IsPaused.Should().BeTrue();
        limits.MaxDownloadSpeedKbps.Should().Be(0);
        limits.MaxUploadSpeedKbps.Should().Be(0);
    }

    [Test]
    public async Task ApplyCurrentLimitsAsync_AppliesLimitsToDownloadEngine()
    {
        var schedule = new SpeedSchedule
        {
            Name = "Throttled",
            Days = 127,
            StartTime = "00:00:00",
            EndTime = "23:59:59",
            MaxDownloadSpeed = 12000,
            MaxUploadSpeed = 4000,
            IsEnabled = true,
            Priority = 1
        };

        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        await _service.ApplyCurrentLimitsAsync();

        await _downloadEngine.Received(1).SetRateLimitsAsync(12000, 4000);
    }

    #endregion
}
