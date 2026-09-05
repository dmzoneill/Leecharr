// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class EngineResourceMetricsTest
{
    private IConfigService configService = null!;
    private IStoragePathService storagePathService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;

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
    }

    [Test]
    public void TorrentResourceMetrics_WhenPropertiesSetToNull_CoercesToNonNullDefaults()
    {
        var metrics = new TorrentResourceMetrics
        {
            Category = null!,
            Status = null!,
            Name = null!,
            InfoHash = null!,
        };

        metrics.Category.Should().Be(string.Empty);
        metrics.Status.Should().Be("Stopped");
        metrics.Name.Should().Be(string.Empty);
        metrics.InfoHash.Should().Be(string.Empty);
    }

    [Test]
    public void MonoTorrentDownloadTask_GetResourceMetrics_WhenCategoryNull_InitializesNonNullCategoryAndStatus()
    {
        var task = new MonoTorrentDownloadTask(1, "hash1", null!, null!);

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.Category.Should().NotBeNull();
        metrics.Category.Should().Be(string.Empty);
        metrics.Status.Should().NotBeNull();
        metrics.Status.Should().Be("Stopped");
        metrics.Name.Should().NotBeNull();
        metrics.InfoHash.Should().Be("hash1");
    }

    [Test]
    public void TransmissionDownloadTask_GetResourceMetrics_DefaultConstructor_InitializesNonNullCategoryAndStatus()
    {
        var task = new TransmissionDownloadTask(10, "hash-trans", "Transmission Torrent", 1024);

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.Category.Should().NotBeNull();
        metrics.Category.Should().Be(string.Empty);
        metrics.Status.Should().NotBeNull();
        metrics.Status.Should().Be("Downloading");
        metrics.Name.Should().Be("Transmission Torrent");
        metrics.InfoHash.Should().Be("hash-trans");
    }

    [Test]
    public void TransmissionDownloadTask_GetResourceMetrics_WhenCategoryProvided_InitializesCorrectCategory()
    {
        var task = new TransmissionDownloadTask(11, "hash-trans2", "Transmission Torrent 2", 2048, "tv");

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.Category.Should().Be("tv");
        metrics.Status.Should().Be("Downloading");
    }

    [Test]
    public async Task EmbeddedTransmissionEngine_GetAllTorrentResourceMetrics_WithNullCategoryTorrent_HasNonNullCategoryAndStatus()
    {
        using var engine = new EmbeddedTransmissionEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        var torrent = new Torrent
        {
            Id = 100,
            InfoHash = "trans-hash-100",
            Name = "Transmission Ingested",
            TotalSize = 5000,
            Category = null!,
        };

        await engine.AddTorrentAsync(torrent);

        var singleMetric = engine.GetTorrentResourceMetrics(100);
        singleMetric.Should().NotBeNull();
        singleMetric.Category.Should().NotBeNull();
        singleMetric.Category.Should().Be(string.Empty);
        singleMetric.Status.Should().NotBeNull();
        singleMetric.Status.Should().Be("Downloading");

        var allMetrics = engine.GetAllTorrentResourceMetrics();
        allMetrics.Should().HaveCount(1);
        allMetrics[0].Category.Should().NotBeNull();
        allMetrics[0].Category.Should().Be(string.Empty);
        allMetrics[0].Status.Should().NotBeNull();
    }

    [Test]
    public void LibTorrentDownloadTask_GetResourceMetrics_DefaultConstructor_InitializesNonNullCategoryAndStatus()
    {
        var task = new LibTorrentDownloadTask(20, "hash-lib", "LibTorrent Download", 4096);

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.Category.Should().NotBeNull();
        metrics.Category.Should().Be(string.Empty);
        metrics.Status.Should().NotBeNull();
        metrics.Status.Should().Be("Downloading");
        metrics.Name.Should().Be("LibTorrent Download");
        metrics.InfoHash.Should().Be("hash-lib");
    }

    [Test]
    public void LibTorrentDownloadTask_GetResourceMetrics_WhenCategoryProvided_InitializesCorrectCategory()
    {
        var task = new LibTorrentDownloadTask(21, "hash-lib2", "LibTorrent Download 2", 8192, "music");

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.Category.Should().Be("music");
        metrics.Status.Should().Be("Downloading");
    }

    [Test]
    public async Task LibTorrentDownloadEngine_GetAllTorrentResourceMetrics_WithNullCategoryTorrent_HasNonNullCategoryAndStatus()
    {
        using var engine = new LibTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        var torrent = new Torrent
        {
            Id = 200,
            InfoHash = "lib-hash-200",
            Name = "LibTorrent Ingested",
            TotalSize = 10000,
            Category = null!,
        };

        await engine.AddTorrentAsync(torrent);

        var singleMetric = engine.GetTorrentResourceMetrics(200);
        singleMetric.Should().NotBeNull();
        singleMetric.Category.Should().NotBeNull();
        singleMetric.Category.Should().Be(string.Empty);
        singleMetric.Status.Should().NotBeNull();
        singleMetric.Status.Should().Be("Downloading");

        var allMetrics = engine.GetAllTorrentResourceMetrics();
        allMetrics.Should().HaveCount(1);
        allMetrics[0].Category.Should().NotBeNull();
        allMetrics[0].Category.Should().Be(string.Empty);
        allMetrics[0].Status.Should().NotBeNull();
    }

    [Test]
    public async Task EmbeddedTransmissionEngine_ProbeHealthAsync_ReturnsUnhealthyAndNotImplemented()
    {
        using var engine = new EmbeddedTransmissionEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        engine.IsAvailable.Should().BeFalse();

        var result = await engine.ProbeHealthAsync();
        result.Should().NotBeNull();
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Contain("not implemented");
    }

    [Test]
    public async Task LibTorrentDownloadEngine_ProbeHealthAsync_ReturnsUnhealthyAndNotImplemented()
    {
        using var engine = new LibTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        engine.IsAvailable.Should().BeFalse();

        var result = await engine.ProbeHealthAsync();
        result.Should().NotBeNull();
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Contain("not implemented");
    }
}
