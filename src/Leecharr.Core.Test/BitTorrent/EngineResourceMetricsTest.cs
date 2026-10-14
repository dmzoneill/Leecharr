// Copyright (c) FeedItOut. All rights reserved.

using System.IO;
using System.Text.Json;
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

    private EmbeddedTransmissionEngine CreateTransmissionEngine()
    {
        return new EmbeddedTransmissionEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            httpClient: DaemonRpcTestDoubles.CreateClient());
    }

    private LibTorrentDownloadEngine CreateLibTorrentEngine()
    {
        return new LibTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            DaemonRpcTestDoubles.CreateClient());
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
        using var engine = this.CreateTransmissionEngine();
        var task = new TransmissionDownloadTask(engine, 10, "hash-trans", "Transmission Torrent", 1024);

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
        using var engine = this.CreateTransmissionEngine();
        var task = new TransmissionDownloadTask(engine, 11, "hash-trans2", "Transmission Torrent 2", 2048, "tv");

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.Category.Should().Be("tv");
        metrics.Status.Should().Be("Downloading");
    }

    [Test]
    public async Task EmbeddedTransmissionEngine_GetAllTorrentResourceMetrics_WithNullCategoryTorrent_HasNonNullCategoryAndStatus()
    {
        using var engine = this.CreateTransmissionEngine();

        var torrent = new Torrent
        {
            Id = 100,
            InfoHash = "1000100010001000100010001000100010001000",
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
    public void LibTorrentDownloadTask_GetResourceMetrics_WithPieceAvailability_ComputesSwarmAvailability()
    {
        using var engine = this.CreateLibTorrentEngine();
        var task = new LibTorrentDownloadTask(engine, 22, "hash-lib3", "LibTorrent Availability", 4096)
        {
            PieceAvailability = new[] { 2, 2, 4, 4 },
        };

        var metrics = task.GetResourceMetrics();

        metrics.SwarmAvailability.Should().Be(3.0);
    }

    [Test]
    public void ResolveConnectedLeechersFromLibtorrentStatus_SubtractsSeedsFromTotalPeers()
    {
        LibTorrentDownloadEngine.ResolveConnectedLeechersFromLibtorrentStatus(8, 10).Should().Be(2);
        LibTorrentDownloadEngine.ResolveConnectedLeechersFromLibtorrentStatus(0, 5).Should().Be(5);
        LibTorrentDownloadEngine.ResolveConnectedLeechersFromLibtorrentStatus(10, 8).Should().Be(0);
    }

    [Test]
    public void MapDaemonTorrentState_Finished_MapsToCompletedNotSeeding()
    {
        LibTorrentDownloadEngine.MapDaemonTorrentState("finished", 1.0).Should().Be(TorrentStatus.Completed);
    }

    [Test]
    public void MapDaemonTorrentState_Seeding_MapsToSeeding()
    {
        LibTorrentDownloadEngine.MapDaemonTorrentState("seeding", 1.0).Should().Be(TorrentStatus.Seeding);
    }

    [Test]
    public void MapDaemonTorrentState_UnknownState_MapsToError()
    {
        LibTorrentDownloadEngine.MapDaemonTorrentState("torrent_status::error", 1.0).Should().Be(TorrentStatus.Error);
    }

    [Test]
    public void MapDaemonTorrentState_DownloadingWhilePaused_MapsToPaused()
    {
        LibTorrentDownloadEngine.MapDaemonTorrentState("downloading", 0.4, paused: true)
            .Should().Be(TorrentStatus.Paused);
    }

    [Test]
    public void MapDaemonTorrentState_SeedingWhilePaused_MapsToPaused()
    {
        LibTorrentDownloadEngine.MapDaemonTorrentState("seeding", 1.0, paused: true)
            .Should().Be(TorrentStatus.Paused);
    }

    [Test]
    public void MapDaemonTorrentState_PausedFlag_PreservesQueuedDemotion()
    {
        LibTorrentDownloadEngine.MapDaemonTorrentState("downloading", 0.2, paused: true, currentStatus: TorrentStatus.Queued)
            .Should().Be(TorrentStatus.Queued);
        LibTorrentDownloadEngine.MapDaemonTorrentState("seeding", 1.0, paused: false, currentStatus: TorrentStatus.Queued)
            .Should().Be(TorrentStatus.Queued);
    }

    [Test]
    public void MapDaemonTorrentState_ClearedPauseFlag_ReplacesLocalPausedWithState()
    {
        LibTorrentDownloadEngine.MapDaemonTorrentState("downloading", 0.2, paused: false, currentStatus: TorrentStatus.Paused)
            .Should().Be(TorrentStatus.Downloading);
    }

    [Test]
    public void IsDaemonTorrentPaused_ReadsPausedFlagNotState()
    {
        using var pausedWhileDownloading = JsonDocument.Parse("{\"state\":\"downloading\",\"paused\":true}");
        using var active = JsonDocument.Parse("{\"state\":\"paused\",\"paused\":false}");
        using var missing = JsonDocument.Parse("{\"state\":\"downloading\"}");

        LibTorrentDownloadEngine.IsDaemonTorrentPaused(pausedWhileDownloading.RootElement).Should().BeTrue();
        LibTorrentDownloadEngine.IsDaemonTorrentPaused(active.RootElement).Should().BeFalse();
        LibTorrentDownloadEngine.IsDaemonTorrentPaused(missing.RootElement).Should().BeFalse();
    }

    [Test]
    public void LibTorrentDownloadTask_GetResourceMetrics_DefaultConstructor_InitializesNonNullCategoryAndStatus()
    {
        using var engine = this.CreateLibTorrentEngine();
        var task = new LibTorrentDownloadTask(engine, 20, "hash-lib", "LibTorrent Download", 4096);

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
        using var engine = this.CreateLibTorrentEngine();
        var task = new LibTorrentDownloadTask(engine, 21, "hash-lib2", "LibTorrent Download 2", 8192, "music");

        var metrics = task.GetResourceMetrics();

        metrics.Should().NotBeNull();
        metrics.Category.Should().Be("music");
        metrics.Status.Should().Be("Downloading");
    }

    [Test]
    public async Task LibTorrentDownloadEngine_GetAllTorrentResourceMetrics_WithNullCategoryTorrent_HasNonNullCategoryAndStatus()
    {
        using var engine = this.CreateLibTorrentEngine();

        var torrent = new Torrent
        {
            Id = 200,
            InfoHash = "2000200020002000200020002000200020002000",
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
    public async Task EmbeddedTransmissionEngine_ProbeHealthAsync_ReturnsValidHealthResult()
    {
        using var engine = this.CreateTransmissionEngine();

        engine.EngineId.Should().Be("Transmission");
        engine.ProtocolName.Should().Be("BitTorrent");

        var result = await engine.ProbeHealthAsync();
        result.Should().NotBeNull();
        result.StatusMessage.Should().NotBeNullOrWhiteSpace();
        result.DependencyChecks.Should().NotBeNull();
    }

    [Test]
    public async Task LibTorrentDownloadEngine_ProbeHealthAsync_ReturnsValidHealthResult()
    {
        using var engine = this.CreateLibTorrentEngine();

        engine.EngineId.Should().Be("LibTorrent");
        engine.ProtocolName.Should().Be("BitTorrent");

        var result = await engine.ProbeHealthAsync();
        result.Should().NotBeNull();
        result.StatusMessage.Should().NotBeNullOrWhiteSpace();
        result.DependencyChecks.Should().NotBeNull();
    }
}
