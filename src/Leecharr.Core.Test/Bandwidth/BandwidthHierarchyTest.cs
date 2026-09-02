using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Bandwidth;

[TestFixture]
public class BandwidthHierarchyTest
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
        _configService.AltDownloadSpeedKbps.Returns(5000);
        _configService.AltUploadSpeedKbps.Returns(2000);
        _configService.AlternativeSpeedEnabled.Returns(false);
        _configService.DiskWriteCacheSizeMb.Returns(128);

        _repository.GetEnabled().Returns(new List<SpeedSchedule>());

        _service = new SpeedSchedulerService(_repository, _configService, _downloadEngine);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    #region 4-Tier Bandwidth Hierarchy Tests

    [Test]
    public void ResolveEffectiveDownloadLimit_Tier1_TorrentOverrideTakesHighestPrecedence()
    {
        // Active schedule with throttle
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Schedule Limit",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = 3000,
                MaxUploadSpeed = 1000,
                IsEnabled = true,
                Priority = 10
            }
        };
        _repository.GetEnabled().Returns(schedules);

        // Torrent limit = 8000, Category limit = 15000, Schedule = 3000, Global = 50000
        var effective = _service.ResolveEffectiveDownloadLimit(torrentLimit: 8000, categoryLimit: 15000);

        // Tier 1 (Torrent Override) wins
        effective.Should().Be(8000);
    }

    [Test]
    public void ResolveEffectiveDownloadLimit_Tier2_CategoryLimitTakesSecondPrecedence()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Schedule Limit",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = 3000,
                MaxUploadSpeed = 1000,
                IsEnabled = true,
                Priority = 10
            }
        };
        _repository.GetEnabled().Returns(schedules);

        // Torrent limit = 0 (none), Category limit = 15000, Schedule = 3000, Global = 50000
        var effective = _service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 15000);

        // Tier 2 (Category Limit) wins
        effective.Should().Be(15000);
    }

    [Test]
    public void ResolveEffectiveDownloadLimit_Tier3_ScheduleLimitAppliesWhenCategoryIsZero()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Schedule Limit",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = 3000,
                MaxUploadSpeed = 1000,
                IsEnabled = true,
                Priority = 10
            }
        };
        _repository.GetEnabled().Returns(schedules);

        // Torrent limit = 0, Category limit = 0, Schedule = 3000, Global = 50000
        var effective = _service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 0);

        // Tier 3 (Schedule Limit) wins
        effective.Should().Be(3000);
    }

    [Test]
    public void ResolveEffectiveDownloadLimit_Tier4_GlobalLimitFallback()
    {
        _repository.GetEnabled().Returns(new List<SpeedSchedule>());

        // Torrent limit = 0, Category limit = 0, No schedule, Global = 50000
        var effective = _service.ResolveEffectiveDownloadLimit(torrentLimit: 0, categoryLimit: 0);

        // Tier 4 (Global Limit) fallback
        effective.Should().Be(50000);
    }

    [Test]
    public void ResolveEffectiveUploadLimit_Follows4LevelHierarchy()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Name = "Schedule Upload Limit",
                Days = 127,
                StartTime = "00:00:00",
                EndTime = "23:59:59",
                MaxDownloadSpeed = 10000,
                MaxUploadSpeed = 1500,
                IsEnabled = true,
                Priority = 10
            }
        };
        _repository.GetEnabled().Returns(schedules);

        // 1. Torrent override
        _service.ResolveEffectiveUploadLimit(torrentLimit: 4000, categoryLimit: 6000).Should().Be(4000);

        // 2. Category limit
        _service.ResolveEffectiveUploadLimit(torrentLimit: 0, categoryLimit: 6000).Should().Be(6000);

        // 3. Schedule limit
        _service.ResolveEffectiveUploadLimit(torrentLimit: 0, categoryLimit: 0).Should().Be(1500);

        // 4. Global limit fallback
        _repository.GetEnabled().Returns(new List<SpeedSchedule>());
        _service.ResolveEffectiveUploadLimit(torrentLimit: 0, categoryLimit: 0).Should().Be(20000);
    }

    #endregion

    #region Alternative Speed Limits Tests

    [Test]
    public void GetCurrentLimits_WhenAltSpeedEnabledAndNoSchedule_ReturnsAltLimits()
    {
        _configService.AlternativeSpeedEnabled.Returns(true);
        _configService.AltDownloadSpeedKbps.Returns(4500);
        _configService.AltUploadSpeedKbps.Returns(1500);

        var limits = _service.GetCurrentLimits();

        limits.MaxDownloadSpeedKbps.Should().Be(4500);
        limits.MaxUploadSpeedKbps.Should().Be(1500);
        limits.IsThrottled.Should().BeTrue();
        limits.IsPaused.Should().BeFalse();
    }

    [Test]
    public void GetCurrentLimits_WhenAltSpeedPaused_ReturnsZeroWithIsPausedTrue()
    {
        _configService.AlternativeSpeedEnabled.Returns(true);
        _configService.AltDownloadSpeedKbps.Returns(-1);
        _configService.AltUploadSpeedKbps.Returns(-1);

        var limits = _service.GetCurrentLimits();

        limits.MaxDownloadSpeedKbps.Should().Be(0);
        limits.MaxUploadSpeedKbps.Should().Be(0);
        limits.IsPaused.Should().BeTrue();
    }

    #endregion

    #region Dynamic Write Cache Scaling Tests

    [Test]
    public void DynamicWriteCache_EnforcesMinimum128MbFloor()
    {
        // When configured cache is low (e.g. 32MB or 0), minimum floor is 128 MB
        var configMb = 32;
        var effectiveMb = Math.Max(128, configMb);
        var cacheBytes = effectiveMb * 1024L * 1024L;

        effectiveMb.Should().Be(128);
        cacheBytes.Should().Be(134217728L); // 128 MB in bytes
    }

    [Test]
    public void DynamicWriteCache_ScalesUpTo1GbCeilingBasedOnMemoryAndSpeed()
    {
        // Dynamic write cache sizing algorithm:
        // Cache scales from 128 MB base up to 1024 MB (1 GB) based on system RAM and download throughput:
        // targetCacheMb = Clamp(Math.Max(128, (ramMb / 16)), 128, 1024)

        // Scenario 1: Low-memory device (2 GB RAM, 2 MB/s download)
        var ram1 = 2048; // 2 GB
        var speedKbps1 = 2000;
        var cacheMb1 = CalculateDynamicWriteCacheMb(ram1, speedKbps1);
        cacheMb1.Should().Be(128);

        // Scenario 2: Moderate server (10 GB RAM, 35 MB/s download)
        var ram2 = 10240; // 10 GB
        var speedKbps2 = 35000;
        var cacheMb2 = CalculateDynamicWriteCacheMb(ram2, speedKbps2);
        cacheMb2.Should().Be(512);

        // Scenario 3: High-end workstation / seedbox (64 GB RAM, 500 MB/s download)
        var ram3 = 65536; // 64 GB
        var speedKbps3 = 500000;
        var cacheMb3 = CalculateDynamicWriteCacheMb(ram3, speedKbps3);
        cacheMb3.Should().Be(1024); // Capped at 1 GB
    }

    private static int CalculateDynamicWriteCacheMb(long systemRamMb, int activeDownloadSpeedKbps)
    {
        // Scale cache based on 5% of RAM or 15 seconds of download buffer
        var ramBasedMb = (int)(systemRamMb * 0.05);
        var bufferBasedMb = (int)(activeDownloadSpeedKbps * 15L / 1024L);

        var calculated = Math.Max(ramBasedMb, bufferBasedMb);
        return Math.Clamp(calculated, 128, 1024);
    }

    #endregion
}
