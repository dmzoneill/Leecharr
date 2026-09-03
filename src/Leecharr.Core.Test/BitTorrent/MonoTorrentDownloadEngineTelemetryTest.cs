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
}
