// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.BitTorrent;
using Leecharr.Api.V1.System;
using Leecharr.Api.V1.TrackerBoost;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class TorrentEngineControllerTest
{
    private ITorrentEngineManager engineManager = null!;
    private IDownloadEngine downloadEngine = null!;
    private ITorrentService torrentService = null!;
    private TorrentEngineController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.engineManager = Substitute.For<ITorrentEngineManager>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.controller = new TorrentEngineController(this.engineManager, this.downloadEngine, this.torrentService);
    }

    [Test]
    public void GetEngines_ReturnsEngineListWithActiveAndAvailableStatuses()
    {
        this.engineManager.ActiveEngineId.Returns("MonoTorrent");

        var engine1 = Substitute.For<ITorrentEngine>();
        engine1.EngineId.Returns("MonoTorrent");
        engine1.DisplayName.Returns("MonoTorrent");
        engine1.Version.Returns("3.0.2");
        engine1.IsAvailable.Returns(true);
        engine1.Description.Returns("C# BitTorrent client");

        var engine2 = Substitute.For<ITorrentEngine>();
        engine2.EngineId.Returns("LibTorrent");
        engine2.DisplayName.Returns("libtorrent");
        engine2.Version.Returns("2.0.9");
        engine2.IsAvailable.Returns(true);
        engine2.Description.Returns("Native C++ engine");

        var engine3 = Substitute.For<ITorrentEngine>();
        engine3.EngineId.Returns("Transmission");
        engine3.DisplayName.Returns("Transmission");
        engine3.Version.Returns("4.0.0");
        engine3.IsAvailable.Returns(false);
        engine3.Description.Returns("Transmission daemon");

        this.engineManager.GetEngines().Returns(new List<ITorrentEngine> { engine1, engine2, engine3 });

        var result = this.controller.GetEngines();

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var engines = ok.Value as List<TorrentEngineResource>;
        engines.Should().NotBeNull();
        engines!.Count.Should().Be(3);

        engines[0].EngineId.Should().Be("MonoTorrent");
        engines[0].IsActive.Should().BeTrue();
        engines[0].Status.Should().Be("Running");

        engines[1].EngineId.Should().Be("LibTorrent");
        engines[1].IsActive.Should().BeFalse();
        engines[1].Status.Should().Be("Ready");

        engines[2].EngineId.Should().Be("Transmission");
        engines[2].IsActive.Should().BeFalse();
        engines[2].Status.Should().Be("Unavailable");
    }

    [Test]
    public void GetActiveEngine_AggregatesSpeedsAndPeers()
    {
        var activeEngine = Substitute.For<ITorrentEngine>();
        activeEngine.EngineId.Returns("MonoTorrent");
        activeEngine.DisplayName.Returns("MonoTorrent");
        activeEngine.Version.Returns("3.0.2");

        this.engineManager.ActiveEngine.Returns(activeEngine);
        this.downloadEngine.ProtocolName.Returns("BitTorrent (MonoTorrent)");

        var task1 = Substitute.For<IDownloadTask>();
        task1.DownloadSpeed.Returns(5000L);
        task1.UploadSpeed.Returns(2000L);
        task1.ConnectedSeeders.Returns(10);
        task1.ConnectedLeechers.Returns(5);

        var task2 = Substitute.For<IDownloadTask>();
        task2.DownloadSpeed.Returns(3000L);
        task2.UploadSpeed.Returns(1000L);
        task2.ConnectedSeeders.Returns(5);
        task2.ConnectedLeechers.Returns(2);

        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { task1, task2 });

        var result = this.controller.GetActiveEngine();

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var status = ok.Value as ActiveEngineStatusResource;
        status.Should().NotBeNull();
        status!.EngineId.Should().Be("MonoTorrent");
        status.DisplayName.Should().Be("MonoTorrent");
        status.Version.Should().Be("3.0.2");
        status.ActiveTorrentsCount.Should().Be(2);
        status.ConnectedPeersCount.Should().Be(22);
        status.DownloadSpeedBytes.Should().Be(8000L);
        status.UploadSpeedBytes.Should().Be(3000L);
        status.ProtocolName.Should().Be("BitTorrent (MonoTorrent)");
    }

    [Test]
    public void GetActiveEngine_WhenActiveEngineIsNull_DefaultsToMonoTorrent()
    {
        this.engineManager.ActiveEngine.Returns((ITorrentEngine)null!);
        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask>());
        this.downloadEngine.ProtocolName.Returns("BitTorrent");

        var result = this.controller.GetActiveEngine();

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var status = ok.Value as ActiveEngineStatusResource;
        status.Should().NotBeNull();
        status!.EngineId.Should().Be("MonoTorrent");
        status.DisplayName.Should().Be("MonoTorrent");
        status.Version.Should().Be("3.0.2");
        status.ActiveTorrentsCount.Should().Be(0);
        status.ConnectedPeersCount.Should().Be(0);
    }

    [Test]
    public async Task SwitchEngine_WhenRequestNullOrEmptyEngineId_ReturnsBadRequest()
    {
        var res1 = await this.controller.SwitchEngine(null!);
        res1.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad1 = (BadRequestObjectResult)res1.Result!;
        var err1 = bad1.Value as SwitchEngineResultResource;
        err1!.Success.Should().BeFalse();
        err1.Error.Should().Be("EngineId is required.");

        var res2 = await this.controller.SwitchEngine(new SwitchEngineRequest { EngineId = "  " });
        res2.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad2 = (BadRequestObjectResult)res2.Result!;
        var err2 = bad2.Value as SwitchEngineResultResource;
        err2!.Success.Should().BeFalse();
        err2.Error.Should().Be("EngineId is required.");
    }

    [Test]
    public async Task SwitchEngine_WhenSwitchFails_ReturnsBadRequestWithResult()
    {
        this.engineManager.SwitchEngineAsync("LibTorrent", true)
            .Returns(Task.FromResult(new EngineSwitchResult
            {
                Success = false,
                Error = "Port 6881 is already in use.",
                PreviousEngine = "MonoTorrent",
                ActiveEngine = "MonoTorrent",
            }));

        var result = await this.controller.SwitchEngine(new SwitchEngineRequest
        {
            EngineId = "LibTorrent",
            PreserveTransfers = true,
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result.Result!;
        var res = bad.Value as SwitchEngineResultResource;
        res.Should().NotBeNull();
        res!.Success.Should().BeFalse();
        res.Error.Should().Be("Port 6881 is already in use.");
        res.PreviousEngine.Should().Be("MonoTorrent");
        res.ActiveEngine.Should().Be("MonoTorrent");
    }

    [Test]
    public async Task SwitchEngine_WhenSwitchSucceeds_ReturnsOkWithResult()
    {
        this.engineManager.SwitchEngineAsync("LibTorrent", true)
            .Returns(Task.FromResult(new EngineSwitchResult
            {
                Success = true,
                PreviousEngine = "MonoTorrent",
                ActiveEngine = "LibTorrent",
                TorrentsMigrated = 7,
                Message = "Switched to LibTorrent successfully.",
            }));

        var result = await this.controller.SwitchEngine(new SwitchEngineRequest
        {
            EngineId = "LibTorrent",
            PreserveTransfers = true,
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var res = ok.Value as SwitchEngineResultResource;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentsMigrated.Should().Be(7);
        res.PreviousEngine.Should().Be("MonoTorrent");
        res.ActiveEngine.Should().Be("LibTorrent");
        res.Message.Should().Be("Switched to LibTorrent successfully.");
    }

    [Test]
    public async Task ProbeEngine_ReturnsProbeResult()
    {
        this.engineManager.ProbeEngineAsync("MonoTorrent")
            .Returns(Task.FromResult(new EngineHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = "MonoTorrent is healthy.",
                DependencyChecks = new List<string> { "Socket OK", "Memory OK" },
                Warnings = new List<string> { "Minor warning" },
            }));

        var result = await this.controller.ProbeEngine("MonoTorrent");

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var probe = ok.Value as EngineProbeResultResource;
        probe.Should().NotBeNull();
        probe!.EngineId.Should().Be("MonoTorrent");
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("MonoTorrent is healthy.");
        probe.DependencyChecks.Should().HaveCount(2);
        probe.Warnings.Should().HaveCount(1);
    }

    [Test]
    public async Task ProbeEnginePost_WithQueryParam_WithRequestBody_AndFallback()
    {
        this.engineManager.ProbeEngineAsync(Arg.Any<string>())
            .Returns(args => Task.FromResult(new EngineHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"Probed {(string)args[0]}",
            }));

        // Query param takes priority
        var res1 = await this.controller.ProbeEnginePost(engineId: "QueryEngine", request: new EngineProbeRequest { EngineId = "BodyEngine" });
        ((EngineProbeResultResource)((OkObjectResult)res1.Result!).Value!).EngineId.Should().Be("QueryEngine");

        // Request body used when query param empty
        var res2 = await this.controller.ProbeEnginePost(engineId: null, request: new EngineProbeRequest { EngineId = "BodyEngine" });
        ((EngineProbeResultResource)((OkObjectResult)res2.Result!).Value!).EngineId.Should().Be("BodyEngine");

        // Fallback to active engine
        this.engineManager.ActiveEngineId.Returns("ActiveEngine");
        var res3 = await this.controller.ProbeEnginePost(engineId: null, request: null);
        ((EngineProbeResultResource)((OkObjectResult)res3.Result!).Value!).EngineId.Should().Be("ActiveEngine");

        // Fallback when active engine is null
        this.engineManager.ActiveEngineId.Returns((string)null!);
        var res4 = await this.controller.ProbeEnginePost(engineId: null, request: null);
        ((EngineProbeResultResource)((OkObjectResult)res4.Result!).Value!).EngineId.Should().Be("MonoTorrent");
    }

    [Test]
    public async Task ProbeEngineGet_WithQueryParam_AndFallback()
    {
        this.engineManager.ProbeEngineAsync(Arg.Any<string>())
            .Returns(args => Task.FromResult(new EngineHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"Probed {(string)args[0]}",
            }));

        var res1 = await this.controller.ProbeEngineGet(engineId: "SpecificEngine");
        ((EngineProbeResultResource)((OkObjectResult)res1.Result!).Value!).EngineId.Should().Be("SpecificEngine");

        this.engineManager.ActiveEngineId.Returns("FallbackActive");
        var res2 = await this.controller.ProbeEngineGet(engineId: null);
        ((EngineProbeResultResource)((OkObjectResult)res2.Result!).Value!).EngineId.Should().Be("FallbackActive");
    }
}

[TestFixture]
public class TrackerBoostControllerTest
{
    private ITrackerBoostService trackerBoostService = null!;
    private TrackerBoostController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.trackerBoostService = Substitute.For<ITrackerBoostService>();
        this.controller = new TrackerBoostController(this.trackerBoostService);
    }

    [Test]
    public async Task GetStatus_CallsService_ReturnsOk()
    {
        var summary = new TrackerBoostStatusSummary
        {
            TotalTrackersMonitored = 20,
            AliveTrackersCount = 18,
            SlowTrackersCount = 2,
        };
        this.trackerBoostService.GetStatusSummaryAsync().Returns(Task.FromResult(summary));

        var result = await this.controller.GetStatus();

        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeSameAs(summary);
    }

    [Test]
    public void GetSettings_And_UpdateSettings_FlowsCorrectly()
    {
        var settings = new TrackerBoostSettings
        {
            AutoBoostEnabled = true,
            IntervalMinutes = 5,
        };
        this.trackerBoostService.GetSettings().Returns(settings);

        var getResult = this.controller.GetSettings();
        getResult.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)getResult).Value.Should().BeSameAs(settings);

        var badUpdate = this.controller.UpdateSettings(null!);
        badUpdate.Should().BeOfType<BadRequestObjectResult>();

        var validUpdate = new TrackerBoostSettings { AutoBoostEnabled = false, IntervalMinutes = 10 };
        var okUpdate = this.controller.UpdateSettings(validUpdate);
        okUpdate.Should().BeOfType<OkObjectResult>();
        this.trackerBoostService.Received(1).UpdateSettings(validUpdate);
    }

    [Test]
    public void GetTrackers_CallsService_ReturnsOk()
    {
        var trackers = new List<TrackerBoostTracker>
        {
            new TrackerBoostTracker { Id = 1, Url = "udp://tracker.open.org:1337" },
        };
        this.trackerBoostService.GetAllTrackers().Returns(trackers);

        var result = this.controller.GetTrackers();

        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeSameAs(trackers);
    }

    [Test]
    public async Task GetCrossMatrix_CallsService_ReturnsOk()
    {
        var matrix = new TrackerCrossMatrixResult();
        this.trackerBoostService.GetCrossMatrixAsync().Returns(Task.FromResult(matrix));

        var result = await this.controller.GetCrossMatrix();

        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeSameAs(matrix);
    }

    [Test]
    public async Task InspectEndpoints_CallServices_ReturnOk()
    {
        var torrentInspection = new TorrentTrackerInspectionResult();
        this.trackerBoostService.InspectTorrentTrackersAsync(42).Returns(Task.FromResult(torrentInspection));

        var res1 = await this.controller.InspectTorrentTrackers(42);
        res1.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)res1).Value.Should().BeSameAs(torrentInspection);

        var hashInspection = new TorrentTrackerInspectionResult();
        this.trackerBoostService.InspectHashTrackersAsync("hash123", "TestName").Returns(Task.FromResult(hashInspection));

        var res2 = await this.controller.InspectHashTrackers("hash123", "TestName");
        res2.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)res2).Value.Should().BeSameAs(hashInspection);
    }

    [Test]
    public void AddTracker_ValidationAndCall()
    {
        this.controller.AddTracker(null!).Should().BeOfType<BadRequestObjectResult>();
        this.controller.AddTracker(new AddTrackerResource { Url = "  " }).Should().BeOfType<BadRequestObjectResult>();

        var tracker = new TrackerBoostTracker { Id = 10, Url = "udp://tracker.open.org:1337" };
        this.trackerBoostService.AddTracker("udp://tracker.open.org:1337", TrackerSourceType.Manual, "Manual Entry")
            .Returns(tracker);

        var result = this.controller.AddTracker(new AddTrackerResource { Url = "udp://tracker.open.org:1337" });
        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeSameAs(tracker);
    }

    [Test]
    public void DeleteTracker_CallsService_ReturnsOk()
    {
        var result = this.controller.DeleteTracker(15);

        result.Should().BeOfType<OkObjectResult>();
        this.trackerBoostService.Received(1).DeleteTracker(15);
    }

    [Test]
    public void BulkImportTrackers_FiltersCommentsAndInvalidUrls()
    {
        this.controller.BulkImportTrackers(null!).Should().BeOfType<BadRequestObjectResult>();
        this.controller.BulkImportTrackers(new BulkImportTrackersResource { TrackersText = " " })
            .Should().BeOfType<BadRequestObjectResult>();

        var text = "# Comment line\n" +
            "\n" +
            "udp://tracker.opentrackr.org:1337/announce\n" +
            "invalid_url_not_uri\n" +
            "http://tracker.open.org:1337/announce\n";

        var result = this.controller.BulkImportTrackers(new BulkImportTrackersResource { TrackersText = text });

        result.Should().BeOfType<OkObjectResult>();
        this.trackerBoostService.Received(1).AddTracker("udp://tracker.opentrackr.org:1337/announce", TrackerSourceType.Manual, "Bulk Import");
        this.trackerBoostService.Received(1).AddTracker("http://tracker.open.org:1337/announce", TrackerSourceType.Manual, "Bulk Import");
    }

    [Test]
    public async Task ScanTrackers_CallsProbeTrackerHealth()
    {
        this.trackerBoostService.ProbeTrackerHealthAsync().Returns(Task.FromResult(12));

        var result = await this.controller.ScanTrackers();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task HarvestEndpoints_CallServices()
    {
        this.trackerBoostService.HarvestFromActiveDownloadsAsync().Returns(Task.FromResult(3));
        this.trackerBoostService.HarvestFromProwlarrAsync().Returns(Task.FromResult(5));
        this.trackerBoostService.HarvestFromCuratedListsAsync().Returns(Task.FromResult(8));

        (await this.controller.HarvestFromDownloads()).Should().BeOfType<OkObjectResult>();
        (await this.controller.HarvestProwlarr()).Should().BeOfType<OkObjectResult>();
        (await this.controller.HarvestFeeds()).Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task BoostEndpoints_CallServices()
    {
        var boostResult = new SwarmBoostResult { Boosted = true, AddedTrackersCount = 3 };
        this.trackerBoostService.BoostTorrentAsync(7, true).Returns(Task.FromResult(boostResult));
        this.trackerBoostService.BoostHashAsync("hash1", "name1", false).Returns(Task.FromResult(boostResult));
        this.trackerBoostService.BoostAllTorrentsAsync(true).Returns(Task.FromResult(new List<SwarmBoostResult> { boostResult }));

        (await this.controller.BoostTorrent(7, true)).Should().BeOfType<OkObjectResult>();
        (await this.controller.BoostHash("hash1", "name1", false)).Should().BeOfType<OkObjectResult>();
        (await this.controller.BoostAllTorrents(true)).Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task InjectTracker_ValidationAndRouting()
    {
        // Null or invalid
        (await this.controller.InjectTracker(null!)).Should().BeOfType<BadRequestObjectResult>();
        (await this.controller.InjectTracker(new InjectTrackerResource { TrackerUrl = "" })).Should().BeOfType<BadRequestObjectResult>();
        (await this.controller.InjectTracker(new InjectTrackerResource { TorrentId = 0, InfoHash = "", TrackerUrl = "udp://tracker.org" }))
            .Should().BeOfType<BadRequestObjectResult>();

        // TorrentId > 0
        var boostResult = new SwarmBoostResult { Boosted = true };
        this.trackerBoostService.InjectTrackerToTorrentAsync(5, "udp://tracker.org", true)
            .Returns(Task.FromResult(boostResult));

        var res1 = await this.controller.InjectTracker(new InjectTrackerResource
        {
            TorrentId = 5,
            TrackerUrl = "udp://tracker.org",
            Force = true,
        });
        res1.Should().BeOfType<OkObjectResult>();

        // InfoHash
        this.trackerBoostService.InjectTrackerToHashAsync("hash99", "udp://tracker.org", false)
            .Returns(Task.FromResult(boostResult));

        var res2 = await this.controller.InjectTracker(new InjectTrackerResource
        {
            TorrentId = 0,
            InfoHash = "hash99",
            TrackerUrl = "udp://tracker.org",
            Force = false,
        });
        res2.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public void Logs_GetAndClear()
    {
        var logEntries = new List<TrackerBoostLogEntry> { new TrackerBoostLogEntry { Id = 1, Message = "Test Log" } };
        this.trackerBoostService.GetLogs(50, "General", "Info").Returns(logEntries);

        var getResult = this.controller.GetLogs(50, "General", "Info");
        getResult.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)getResult).Value.Should().BeSameAs(logEntries);

        var clearResult = this.controller.ClearLogs();
        clearResult.Should().BeOfType<OkObjectResult>();
        this.trackerBoostService.Received(1).ClearLogs();
    }
}

[TestFixture]
public class LogControllerTest
{
    private IConfigService configService;
    private LogController controller;
    private RingBufferTarget originalTarget;

    private class TestRingBufferTarget : RingBufferTarget
    {
        public TestRingBufferTarget(int capacity = 2048)
            : base(capacity)
        {
        }

        public void AddLog(LogLevel level, string loggerName, string message, Exception ex = null)
        {
            this.Write(new LogEventInfo(level, loggerName, message) { Exception = ex });
        }
    }

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.controller = new LogController(this.configService);
        this.originalTarget = RingBufferTarget.Instance;
    }

    [TearDown]
    public void TearDown()
    {
        RingBufferTarget.Instance = this.originalTarget!;
    }

    [Test]
    public void GetLogs_WhenRingBufferNull_ReturnsEmptyList()
    {
        RingBufferTarget.Instance = null!;

        var result = this.controller.GetLogs();

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var logs = ok.Value as List<LogResource>;
        logs.Should().NotBeNull();
        logs.Should().BeEmpty();
    }

    [Test]
    public void GetLogs_WithEntriesInRingBuffer_MapsProperties()
    {
        var testTarget = new TestRingBufferTarget();
        testTarget.AddLog(LogLevel.Info, "Leecharr.Test", "Sample log message", new InvalidOperationException("Test exception"));
        RingBufferTarget.Instance = testTarget;

        var result = this.controller.GetLogs("all", 100);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var logs = ok.Value as List<LogResource>;
        logs.Should().NotBeNull();
        logs!.Count.Should().BeGreaterThanOrEqualTo(1);

        var entry = logs.First(l => l.Message == "Sample log message");
        entry.Level.Should().Be("Info");
        entry.Logger.Should().Be("Leecharr.Test");
        entry.Exception.Should().Contain("Test exception");
        entry.Time.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void GetLogs_ClampsCountBetween1And5000()
    {
        var testTarget = new TestRingBufferTarget();
        testTarget.AddLog(LogLevel.Info, "Leecharr.Test", "Clamped test");
        RingBufferTarget.Instance = testTarget;

        // Count < 1 clamped to 1
        var res1 = this.controller.GetLogs(count: -10);
        res1.Result.Should().BeOfType<OkObjectResult>();

        // Count > 5000 clamped to 5000
        var res2 = this.controller.GetLogs(count: 99999);
        res2.Result.Should().BeOfType<OkObjectResult>();
    }

    [TestCase("Trace")]
    [TestCase("Debug")]
    [TestCase("Info")]
    [TestCase("Warn")]
    [TestCase("Error")]
    [TestCase("Fatal")]
    [TestCase("all")]
    [TestCase(null)]
    [TestCase("invalid_level_string")]
    public void GetLogs_ParsesLogLevelsWithoutThrowing(string level)
    {
        var testTarget = new TestRingBufferTarget();
        testTarget.AddLog(LogLevel.Warn, "Leecharr.Test", "Level parse test");
        RingBufferTarget.Instance = testTarget;

        var result = this.controller.GetLogs(level: level, count: 50);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
