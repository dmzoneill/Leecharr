// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using MonoTorrent.Client;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class MonoTorrentDownloadEngineTelemetryTest
{
    private IConfigService configService = null!;
    private IStoragePathService storagePathService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;
    private MonoTorrentDownloadEngine engine = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configService.ListeningPort.Returns(0);
        this.configService.UpnpEnabled.Returns(false);
        this.configService.DiskWriteCacheSizeMb.Returns(128);
        this.configService.DownloadDir.Returns(Path.GetTempPath());
        this.configService.MaxPerTorrentConnections.Returns(50);
        this.configService.MaxUploadSlots.Returns(4);

        this.storagePathService = Substitute.For<IStoragePathService>();
        this.storagePathService.GetIncompleteDirectory().Returns(Path.GetTempPath());

        this.categoryService = Substitute.For<ICategoryService>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.eventAggregator = Substitute.For<IEventAggregator>();

        this.engine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.engine?.Dispose();
    }

    [Test]
    public void GetEngineMetrics_WhenNoTorrents_ReturnsValidDefaultMetrics()
    {
        var metrics = this.engine.GetEngineMetrics();

        metrics.Should().NotBeNull();
        metrics.EngineId.Should().Be("MonoTorrent");
        metrics.DisplayName.Should().Contain("MonoTorrent");
        metrics.ActiveTorrents.Should().Be(0);
        metrics.TotalDownloadSpeed.Should().Be(0);
        metrics.TotalUploadSpeed.Should().Be(0);
        metrics.DiskCacheCapacityBytes.Should().BeGreaterThan(0);
        metrics.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public void GetTorrentResourceMetrics_WhenTorrentNotFound_ReturnsNull()
    {
        var metrics = this.engine.GetTorrentResourceMetrics(999);

        metrics.Should().BeNull();
    }

    [Test]
    public void GetAllTorrentResourceMetrics_WhenEmpty_ReturnsEmptyList()
    {
        var list = this.engine.GetAllTorrentResourceMetrics();

        list.Should().NotBeNull();
        list.Should().BeEmpty();
    }

    [Test]
    public void MonoTorrentDownloadTask_GetResourceMetrics_WhenManagerNull_ReturnsStoppedMetrics()
    {
        var task = new MonoTorrentDownloadTask(42, "0123456789abcdef0123456789abcdef01234567", null!, "movies");

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.TorrentId.Should().Be(42);
        metrics.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        metrics.Category.Should().Be("movies");
        metrics.Status.Should().Be("Stopped");
        metrics.Progress.Should().Be(0.0);
    }

    [Test]
    public void TryExtractDiskManagerMetrics_WhenDiskManagerHasCacheMisses_ExtractsCorrectValues()
    {
        var method = typeof(MonoTorrentDownloadEngine).GetMethod("TryExtractDiskManagerMetrics", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var mockDisk = new
        {
            CacheHits = 75L,
            CacheMisses = 25L,
            CacheUsed = 1048576L,
            PendingWriteBytes = 32768,
            PendingReadBytes = 16384,
            TotalBytesRead = 204800L,
            TotalBytesWritten = 409600L,
            ReadRate = 1024L,
            WriteRate = 2048L,
        };

        var mockEngine = new
        {
            DiskManager = mockDisk,
        };

        var parameters = new object[] { mockEngine, 0L, 0L, 0L, 0, 0, 0L, 0L, 0L, 0L };
        method!.Invoke(null, parameters);

        var cacheHits = (long)parameters[1];
        var cacheMisses = (long)parameters[2];
        var cacheUsed = (long)parameters[3];
        var pendingWrites = (int)parameters[4];
        var pendingReads = (int)parameters[5];

        cacheHits.Should().Be(75L);
        cacheMisses.Should().Be(25L);
        cacheUsed.Should().Be(1048576L);
        pendingWrites.Should().Be(2);
        pendingReads.Should().Be(1);

        var totalCacheAccesses = cacheHits + cacheMisses;
        var hitRatio = totalCacheAccesses > 0 ? Math.Round(((double)cacheHits / totalCacheAccesses) * 100.0, 1) : 100.0;
        hitRatio.Should().Be(75.0);
    }

    [Test]
    public void TryExtractDiskManagerMetrics_WhenDiskManagerHasCacheMissFallback_ExtractsCorrectValues()
    {
        var method = typeof(MonoTorrentDownloadEngine).GetMethod("TryExtractDiskManagerMetrics", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var mockDisk = new
        {
            CacheHits = 80L,
            CacheMiss = 20L,
            CacheBytesUsed = 524288L,
        };

        var mockEngine = new
        {
            DiskManager = mockDisk,
        };

        var parameters = new object[] { mockEngine, 0L, 0L, 0L, 0, 0, 0L, 0L, 0L, 0L };
        method!.Invoke(null, parameters);

        var cacheHits = (long)parameters[1];
        var cacheMisses = (long)parameters[2];
        var cacheUsed = (long)parameters[3];

        cacheHits.Should().Be(80L);
        cacheMisses.Should().Be(20L);
        cacheUsed.Should().Be(524288L);

        var totalCacheAccesses = cacheHits + cacheMisses;
        var hitRatio = totalCacheAccesses > 0 ? Math.Round(((double)cacheHits / totalCacheAccesses) * 100.0, 1) : 100.0;
        hitRatio.Should().Be(80.0);
    }
}
