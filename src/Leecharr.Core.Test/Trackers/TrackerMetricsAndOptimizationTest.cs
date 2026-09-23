// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Trackers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;

namespace Leecharr.Core.Test.Trackers;

[TestFixture]
public class TrackerMetricsAndOptimizationTest
{
    private ITrackerMetricRepository metricRepository = null!;
    private ITrackerMetricSnapshotRepository snapshotRepository = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private ITorrentRepository torrentRepository = null!;
    private IEventAggregator eventAggregator = null!;
    private TrackerMetricService metricService = null!;

    private List<TrackerMetric> storedMetrics = null!;
    private List<TrackerMetricSnapshot> storedSnapshots = null!;
    private List<TrackerEntry> storedEntries = null!;
    private List<Torrent> storedTorrents = null!;
    private List<object> publishedEvents = null!;

    [SetUp]
    public void SetUp()
    {
        this.storedMetrics = new List<TrackerMetric>();
        this.storedSnapshots = new List<TrackerMetricSnapshot>();
        this.storedEntries = new List<TrackerEntry>();
        this.storedTorrents = new List<Torrent>();
        this.publishedEvents = new List<object>();

        this.metricRepository = Substitute.For<ITrackerMetricRepository>();
        this.metricRepository.All().Returns(_ => this.storedMetrics.ToList());
        this.metricRepository.Get(Arg.Any<int>()).Returns(ci =>
        {
            var id = ci.Arg<int>();
            return this.storedMetrics.FirstOrDefault(m => m.Id == id);
        });
        this.metricRepository.FindByUrl(Arg.Any<string>()).Returns(ci =>
        {
            var u = ci.Arg<string>()?.Trim();
            return this.storedMetrics.FirstOrDefault(m => string.Equals(m.TrackerUrl, u, StringComparison.OrdinalIgnoreCase));
        });
        this.metricRepository.Insert(Arg.Any<TrackerMetric>()).Returns(ci =>
        {
            var m = ci.Arg<TrackerMetric>();
            m.Id = this.storedMetrics.Count + 1;
            this.storedMetrics.Add(m);
            return m;
        });
        this.metricRepository.When(r => r.Update(Arg.Any<TrackerMetric>())).Do(ci =>
        {
            var updated = ci.Arg<TrackerMetric>();
            var index = this.storedMetrics.FindIndex(m => m.Id == updated.Id);
            if (index >= 0)
            {
                this.storedMetrics[index] = updated;
            }
        });
        this.metricRepository.When(r => r.Delete(Arg.Any<int>())).Do(ci =>
        {
            var id = ci.Arg<int>();
            this.storedMetrics.RemoveAll(m => m.Id == id);
        });
        this.metricRepository.When(r => r.ResetStats(Arg.Any<int>())).Do(ci =>
        {
            var id = ci.Arg<int>();
            var item = this.storedMetrics.FirstOrDefault(m => m.Id == id);
            if (item != null)
            {
                item.TotalAnnounces = 0;
                item.SuccessfulAnnounces = 0;
                item.FailedAnnounces = 0;
                item.TotalScrapes = 0;
                item.SuccessfulScrapes = 0;
                item.FailedScrapes = 0;
                item.TotalUploaded = 0;
                item.TotalDownloaded = 0;
                item.SessionUploaded = 0;
                item.SessionDownloaded = 0;
                item.TotalPeersDiscovered = 0;
            }
        });

        this.snapshotRepository = Substitute.For<ITrackerMetricSnapshotRepository>();
        this.snapshotRepository.GetRecentSnapshots(Arg.Any<DateTime>()).Returns(ci =>
        {
            var since = ci.Arg<DateTime>();
            return this.storedSnapshots.Where(s => s.Timestamp >= since).ToList();
        });
        this.snapshotRepository.GetHistory(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<int>()).Returns(ci =>
        {
            var metricId = ci.ArgAt<int>(0);
            var since = ci.ArgAt<DateTime>(1);
            var limit = ci.ArgAt<int>(2);
            return this.storedSnapshots
                .Where(s => s.TrackerMetricId == metricId && s.Timestamp >= since)
                .Take(limit)
                .ToList();
        });
        this.snapshotRepository.When(r => r.InsertMany(Arg.Any<IList<TrackerMetricSnapshot>>())).Do(ci =>
        {
            var items = ci.Arg<IList<TrackerMetricSnapshot>>();
            this.storedSnapshots.AddRange(items);
        });
        this.snapshotRepository.When(r => r.DeleteByMetricId(Arg.Any<int>())).Do(ci =>
        {
            var id = ci.Arg<int>();
            this.storedSnapshots.RemoveAll(s => s.TrackerMetricId == id);
        });

        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.trackerEntryRepository.All().Returns(_ => this.storedEntries.ToList());

        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.torrentRepository.All().Returns(_ => this.storedTorrents.ToList());

        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<object>())).Do(ci =>
        {
            this.publishedEvents.Add(ci.Arg<object>());
        });

        this.metricService = new TrackerMetricService(
            this.metricRepository,
            this.snapshotRepository,
            this.trackerEntryRepository,
            this.torrentRepository,
            this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.metricService?.Dispose();
    }

    [Test]
    public void RecordAnnounce_Success_UpdatesMetricsAndPublishesEvents()
    {
        var trackerUrl = "udp://tracker.opentrackr.org:1337/announce";
        var metric = this.metricService.RecordAnnounce(
            trackerUrl,
            torrentId: 1,
            uploaded: 5000,
            downloaded: 10000,
            left: 20000,
            responseTimeMs: 120,
            success: true,
            seeders: 45,
            leechers: 12,
            peersCount: 57);

        metric.Should().NotBeNull();
        metric.TotalAnnounces.Should().Be(1);
        metric.SuccessfulAnnounces.Should().Be(1);
        metric.FailedAnnounces.Should().Be(0);
        metric.ConsecutiveFailures.Should().Be(0);
        metric.Status.Should().Be("Working");
        metric.LastSeeders.Should().Be(45);
        metric.LastLeechers.Should().Be(12);
        metric.LastPeers.Should().Be(57);
        metric.TotalPeersDiscovered.Should().Be(57);
        metric.TotalUploaded.Should().Be(5000);
        metric.TotalDownloaded.Should().Be(10000);
        metric.TotalLeft.Should().Be(20000);
        metric.LastResponseTimeMs.Should().Be(120);

        this.publishedEvents.Should().ContainSingle(e => e is TrackerMetricUpdatedEvent);
        var updatedEvent = (TrackerMetricUpdatedEvent)this.publishedEvents.First(e => e is TrackerMetricUpdatedEvent);
        updatedEvent.TrackerUrl.Should().Be(trackerUrl);
        updatedEvent.TotalAnnounces.Should().Be(1);
        updatedEvent.Status.Should().Be("Working");
    }

    [Test]
    public void RecordAnnounce_DeltaBytesCalculation_TracksSubsequentAnnouncesAccurately()
    {
        var trackerUrl = "http://tracker.example.com:8080/announce";

        // First announce
        this.metricService.RecordAnnounce(
            trackerUrl,
            torrentId: 10,
            uploaded: 1000,
            downloaded: 2000,
            left: 5000,
            responseTimeMs: 100,
            success: true,
            seeders: 5,
            leechers: 2,
            peersCount: 7);

        // Second announce with higher cumulative counts
        var second = this.metricService.RecordAnnounce(
            trackerUrl,
            torrentId: 10,
            uploaded: 1800,
            downloaded: 3500,
            left: 3500,
            responseTimeMs: 110,
            success: true,
            seeders: 6,
            leechers: 1,
            peersCount: 7);

        second.TotalUploaded.Should().Be(1800);
        second.TotalDownloaded.Should().Be(3500);
        second.TotalAnnounces.Should().Be(2);
        second.SuccessfulAnnounces.Should().Be(2);
    }

    [Test]
    public void RecordAnnounce_Failure_TransitionsThroughDegradedAndOfflineStatuses()
    {
        var trackerUrl = "udp://failing.tracker.org:1337/announce";

        // 1st failure -> still Working
        var m1 = this.metricService.RecordAnnounce(trackerUrl, 1, 0, 0, 0, 0, false, 0, 0, 0, "Timeout");
        m1.ConsecutiveFailures.Should().Be(1);
        m1.FailedAnnounces.Should().Be(1);
        m1.Status.Should().Be("Working");

        // 2nd failure -> Degraded
        var m2 = this.metricService.RecordAnnounce(trackerUrl, 1, 0, 0, 0, 0, false, 0, 0, 0, "Timeout");
        m2.ConsecutiveFailures.Should().Be(2);
        m2.Status.Should().Be("Degraded");

        // 3rd & 4th failure -> still Degraded
        this.metricService.RecordAnnounce(trackerUrl, 1, 0, 0, 0, 0, false, 0, 0, 0, "Timeout");
        this.metricService.RecordAnnounce(trackerUrl, 1, 0, 0, 0, 0, false, 0, 0, 0, "Timeout");

        // 5th failure -> Offline
        var m5 = this.metricService.RecordAnnounce(trackerUrl, 1, 0, 0, 0, 0, false, 0, 0, 0, "Connection refused");
        m5.ConsecutiveFailures.Should().Be(5);
        m5.Status.Should().Be("Offline");
        m5.LastErrorMessage.Should().Be("Connection refused");

        this.publishedEvents.Should().Contain(e => e is TrackerUnreachableEvent);
    }

    [Test]
    public void RecordAnnounce_NullOrWhitespaceUrl_ReturnsNullWithoutException()
    {
        var res1 = this.metricService.RecordAnnounce(null, 1, 0, 0, 0, 0, true, 0, 0, 0);
        var res2 = this.metricService.RecordAnnounce(string.Empty, 1, 0, 0, 0, 0, true, 0, 0, 0);
        var res3 = this.metricService.RecordAnnounce("   ", 1, 0, 0, 0, 0, true, 0, 0, 0);

        res1.Should().BeNull();
        res2.Should().BeNull();
        res3.Should().BeNull();
        this.storedMetrics.Should().BeEmpty();
    }

    [Test]
    public void RecordScrape_Success_UpdatesScrapeCountsAndMetrics()
    {
        var trackerUrl = "http://scrape.example.org:6969/announce";
        var metric = this.metricService.RecordScrape(
            trackerUrl,
            responseTimeMs: 85,
            success: true,
            seeders: 50,
            leechers: 20,
            completed: 100);

        metric.Should().NotBeNull();
        metric.TotalScrapes.Should().Be(1);
        metric.SuccessfulScrapes.Should().Be(1);
        metric.FailedScrapes.Should().Be(0);
        metric.ConsecutiveFailures.Should().Be(0);
        metric.Status.Should().Be("Working");
        metric.LastSeeders.Should().Be(50);
        metric.LastLeechers.Should().Be(20);
        metric.LastResponseTimeMs.Should().Be(85);

        this.publishedEvents.Should().ContainSingle(e => e is TrackerMetricUpdatedEvent);
    }

    [Test]
    public void RecordScrape_Failure_IncrementsFailedScrapesAndSetsErrorMessage()
    {
        var trackerUrl = "udp://scrape.failing.org:1337/announce";
        var metric = this.metricService.RecordScrape(
            trackerUrl,
            responseTimeMs: 0,
            success: false,
            seeders: 0,
            leechers: 0,
            completed: 0,
            error: "Scrape unreachable");

        metric.Should().NotBeNull();
        metric.TotalScrapes.Should().Be(1);
        metric.SuccessfulScrapes.Should().Be(0);
        metric.FailedScrapes.Should().Be(1);
        metric.LastErrorMessage.Should().Be("Scrape unreachable");
    }

    [Test]
    public void RecordScrape_NullOrWhitespaceUrl_ReturnsNull()
    {
        var res = this.metricService.RecordScrape("  ", 50, true, 0, 0, 0);
        res.Should().BeNull();
    }

    [Test]
    public void GetMetricsByTracker_RetrievesAllAndSingleMetricsWithPercentiles()
    {
        var metric1 = new TrackerMetric
        {
            Id = 1,
            TrackerUrl = "udp://fast.tracker.org:1337/announce",
            Host = "fast.tracker.org",
            Domain = "tracker.org",
            Protocol = "udp",
            TotalUploaded = 50000,
            TotalAnnounces = 100,
            SuccessfulAnnounces = 98,
            AvgResponseTimeMs = 50,
            Status = "Working",
        };
        var metric2 = new TrackerMetric
        {
            Id = 2,
            TrackerUrl = "http://slow.tracker.com:80/announce",
            Host = "slow.tracker.com",
            Domain = "tracker.com",
            Protocol = "http",
            TotalUploaded = 10000,
            TotalAnnounces = 50,
            SuccessfulAnnounces = 40,
            AvgResponseTimeMs = 450,
            Status = "Working",
        };
        this.storedMetrics.Add(metric1);
        this.storedMetrics.Add(metric2);

        this.storedSnapshots.Add(new TrackerMetricSnapshot { TrackerMetricId = 1, Timestamp = DateTime.UtcNow, ResponseTimeMs = 45, IsSuccess = true });
        this.storedSnapshots.Add(new TrackerMetricSnapshot { TrackerMetricId = 1, Timestamp = DateTime.UtcNow, ResponseTimeMs = 55, IsSuccess = true });

        var all = this.metricService.GetAllMetrics();
        all.Should().HaveCount(2);
        all[0].Id.Should().Be(1); // Higher upload first
        all[1].Id.Should().Be(2);

        var byId = this.metricService.GetMetric(1);
        byId.Should().NotBeNull();
        byId!.TrackerUrl.Should().Be("udp://fast.tracker.org:1337/announce");

        var byUrl = this.metricService.GetMetricByUrl("http://slow.tracker.com:80/announce");
        byUrl.Should().NotBeNull();
        byUrl!.Id.Should().Be(2);

        var missing = this.metricService.GetMetric(999);
        missing.Should().BeNull();
    }

    [Test]
    public void GetHistory_ClampsHoursAndLimitsWithinSafeRanges()
    {
        var metric = new TrackerMetric
        {
            Id = 10,
            TrackerUrl = "udp://history.tracker.org:1337/announce",
            Status = "Working",
        };
        this.storedMetrics.Add(metric);

        // Clamped: hours 0 -> 1, limit 0 -> 1
        var historyClampedLow = this.metricService.GetHistory(10, hours: 0, limit: 0);
        historyClampedLow.Should().NotBeNull();
        this.snapshotRepository.Received(1).GetHistory(10, Arg.Is<DateTime>(dt => dt <= DateTime.UtcNow.AddMinutes(-59)), 1);

        // Clamped: hours 500 -> 168, limit 5000 -> 2000
        var historyClampedHigh = this.metricService.GetHistory(10, hours: 500, limit: 5000);
        historyClampedHigh.Should().NotBeNull();
        this.snapshotRepository.Received(1).GetHistory(10, Arg.Is<DateTime>(dt => dt <= DateTime.UtcNow.AddHours(-167)), 2000);
    }

    [Test]
    public void ResetMetrics_And_DeleteMetric_InteractsWithRepositoryAndCaches()
    {
        var metric = new TrackerMetric
        {
            Id = 5,
            TrackerUrl = "udp://reset.tracker.org:1337/announce",
            TotalAnnounces = 10,
            TotalUploaded = 2000,
        };
        this.storedMetrics.Add(metric);

        this.metricService.ResetMetrics(5);
        this.metricRepository.Received(1).ResetStats(5);

        this.metricService.DeleteMetric(5);
        this.snapshotRepository.Received(1).DeleteByMetricId(5);
        this.metricRepository.Received(1).Delete(5);
    }

    [Test]
    public void GetAggregateSwarmHealth_ReturnsAccurateMetricsSummary()
    {
        this.storedMetrics.Add(new TrackerMetric
        {
            Id = 1,
            TrackerUrl = "udp://tracker1.org:1337/announce",
            Protocol = "udp",
            Status = "Working",
            TotalUploaded = 1000,
            TotalDownloaded = 500,
            TotalAnnounces = 20,
            SuccessfulAnnounces = 20,
            TotalScrapes = 10,
            SuccessfulScrapes = 10,
            TotalPeersDiscovered = 100,
            AvgResponseTimeMs = 60,
        });

        this.storedMetrics.Add(new TrackerMetric
        {
            Id = 2,
            TrackerUrl = "http://tracker2.com:80/announce",
            Protocol = "http",
            Status = "Degraded",
            TotalUploaded = 2000,
            TotalDownloaded = 1000,
            TotalAnnounces = 15,
            SuccessfulAnnounces = 10,
            FailedAnnounces = 5,
            TotalScrapes = 5,
            SuccessfulScrapes = 3,
            TotalPeersDiscovered = 50,
            AvgResponseTimeMs = 250,
        });

        this.storedMetrics.Add(new TrackerMetric
        {
            Id = 3,
            TrackerUrl = "https://tracker3.net:443/announce",
            Protocol = "https",
            Status = "Offline",
            TotalUploaded = 0,
            TotalDownloaded = 0,
            TotalAnnounces = 5,
            SuccessfulAnnounces = 0,
            FailedAnnounces = 5,
            TotalScrapes = 2,
            SuccessfulScrapes = 0,
            TotalPeersDiscovered = 0,
            AvgResponseTimeMs = 0,
        });

        var summary = this.metricService.GetSummary();

        summary.Should().NotBeNull();
        summary.TotalTrackers.Should().Be(3);
        summary.HealthyTrackers.Should().Be(1);
        summary.DegradedTrackers.Should().Be(1);
        summary.OfflineTrackers.Should().Be(1);
        summary.TotalUploaded.Should().Be(3000);
        summary.TotalDownloaded.Should().Be(1500);
        summary.GlobalRatio.Should().Be(2.0);
        summary.TotalAnnounces.Should().Be(40);
        summary.SuccessfulAnnounces.Should().Be(30);
        summary.FailedAnnounces.Should().Be(10);
        summary.AnnounceSuccessRate.Should().Be(75.0);
        summary.TotalScrapes.Should().Be(17);
        summary.SuccessfulScrapes.Should().Be(13);
        summary.TotalPeersDiscovered.Should().Be(150);

        summary.ProtocolDistribution.Should().ContainKey("UDP");
        summary.ProtocolDistribution.Should().ContainKey("HTTP");
        summary.ProtocolDistribution.Should().ContainKey("HTTPS");
        summary.HealthDistribution.Should().ContainKey("Working");
        summary.HealthDistribution.Should().ContainKey("Degraded");
        summary.HealthDistribution.Should().ContainKey("Offline");

        summary.TopUploadTrackers.Should().NotBeEmpty();
        summary.TopUploadTrackers[0].TrackerUrl.Should().Be("http://tracker2.com:80/announce");
        summary.TopPeerTrackers.Should().NotBeEmpty();
        summary.TopPeerTrackers[0].TrackerUrl.Should().Be("udp://tracker1.org:1337/announce");
    }

    [Test]
    public void SeedFromExistingTrackers_PopulatesMetricsFromTorrentsAndEntries()
    {
        this.storedTorrents.Add(new Torrent
        {
            Id = 1,
            TrackerUrl = "udp://seeded1.tracker.org:1337/announce",
            Uploaded = 4000,
            Downloaded = 2000,
        });

        this.storedEntries.Add(new TrackerEntry
        {
            TorrentId = 1,
            Url = "http://seeded2.tracker.com:80/announce",
            TotalAnnounces = 12,
            SuccessfulAnnounces = 10,
            LastResponseTime = 95,
            Downloaded = 1500,
        });

        this.metricService.SeedFromExistingTrackers();

        this.storedMetrics.Should().Contain(m => m.TrackerUrl == "udp://seeded1.tracker.org:1337/announce");
        this.storedMetrics.Should().Contain(m => m.TrackerUrl == "http://seeded2.tracker.com:80/announce");

        var seededEntry = this.storedMetrics.First(m => m.TrackerUrl == "http://seeded2.tracker.com:80/announce");
        seededEntry.TotalAnnounces.Should().Be(12);
        seededEntry.SuccessfulAnnounces.Should().Be(10);
        seededEntry.FailedAnnounces.Should().Be(2);
        seededEntry.LastResponseTimeMs.Should().Be(95);
    }

    [Test]
    public void PruneSnapshots_CallsRepositoryPruneOlderThan()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        this.metricService.PruneSnapshots(cutoff);

        this.snapshotRepository.Received(1).PruneOlderThan(cutoff);
    }

    [Test]
    public void CalculatePercentile_EdgeCasesAndInterpolation()
    {
        TrackerMetricService.CalculatePercentile(null, 50).Should().Be(0.0);
        TrackerMetricService.CalculatePercentile(Array.Empty<double>(), 50).Should().Be(0.0);
        TrackerMetricService.CalculatePercentile(new double[] { 42.0 }, 90).Should().Be(42.0);
        TrackerMetricService.CalculatePercentile(new double[] { 10.0, 20.0, 30.0 }, 0).Should().Be(10.0);
        TrackerMetricService.CalculatePercentile(new double[] { 10.0, 20.0, 30.0 }, 100).Should().Be(30.0);

        var values = new double[] { 10.0, 20.0, 30.0, 40.0, 50.0 };
        TrackerMetricService.CalculatePercentile(values, 50).Should().Be(30.0);
    }

    [Test]
    public void DynamicTierCalculation_AndPruning_AssignsExpectedTiers()
    {
        // Alive & fast (< 300ms) -> Tier 1
        TrackerBoostService.CalculateDynamicTier(TrackerHealthStatus.Alive, 80).Should().Be(1);
        TrackerBoostService.CalculateDynamicTier(TrackerHealthStatus.Alive, 290).Should().Be(1);

        // Alive & normal / slow (>= 300ms) -> Tier 2
        TrackerBoostService.CalculateDynamicTier(TrackerHealthStatus.Alive, 300).Should().Be(2);
        TrackerBoostService.CalculateDynamicTier(TrackerHealthStatus.Slow, 500).Should().Be(2);

        // Offline or Untested -> Tier 3
        TrackerBoostService.CalculateDynamicTier(TrackerHealthStatus.Offline, 50).Should().Be(3);
        TrackerBoostService.CalculateDynamicTier(TrackerHealthStatus.Untested, 0).Should().Be(3);

        // Tier 0 is never returned (BEP 12 canonical tracker reservation)
        foreach (TrackerHealthStatus status in Enum.GetValues(typeof(TrackerHealthStatus)))
        {
            for (var latency = 0; latency <= 1000; latency += 100)
            {
                TrackerBoostService.CalculateDynamicTier(status, latency).Should().BeGreaterThanOrEqualTo(1);
            }
        }
    }

    [Test]
    public async Task TrackerBoostOptimizationTask_ExecutesCycle_GuardsReentrancy_AndHandlesExceptions()
    {
        var mockBoostService = Substitute.For<ITrackerBoostService>();
        mockBoostService.GetSettings().Returns(new TrackerBoostSettings { IntervalMinutes = 60 });

        var tcs = new TaskCompletionSource<bool>();
        var executions = 0;

        mockBoostService.RunOptimizationCycleAsync().Returns(async _ =>
        {
            Interlocked.Increment(ref executions);
            await tcs.Task;
        });

        using var task = new TrackerBoostOptimizationTask(mockBoostService);

        // Launch first run (blocked on tcs)
        var runTask1 = task.ExecuteAsync();

        // Launch second run while first is still running
        var runTask2 = task.ExecuteAsync();

        // Second run must exit immediately due to semaphore guard
        runTask2.IsCompleted.Should().BeTrue();

        tcs.SetResult(true);
        await runTask1;

        executions.Should().Be(1);

        // Test exception handling: should catch and log without unhandled exception
        mockBoostService.RunOptimizationCycleAsync().Returns(Task.FromException(new InvalidOperationException("Cycle test error")));
        var act = async () => await task.ExecuteAsync();
        await act.Should().NotThrowAsync();
    }

    [Test]
    public void TrackerBoostOptimizationTask_HandleApplicationStarted_And_ExecuteFireAndForget()
    {
        var mockBoostService = Substitute.For<ITrackerBoostService>();
        mockBoostService.GetSettings().Returns(new TrackerBoostSettings { IntervalMinutes = 120 });

        using var task = new TrackerBoostOptimizationTask(mockBoostService);

        var act1 = () => task.Handle(new ApplicationStartedEvent());
        act1.Should().NotThrow();

        var act2 = () => task.Execute();
        act2.Should().NotThrow();
    }

    [Test]
    public void TrackerMetricsController_GetAll_ReturnsOkWithMappedResources()
    {
        var metric = new TrackerMetric
        {
            Id = 1,
            TrackerUrl = "udp://controller.tracker.org:1337/announce",
            Host = "controller.tracker.org",
            Domain = "tracker.org",
            Protocol = "udp",
            TotalAnnounces = 10,
            SuccessfulAnnounces = 10,
            Status = "Working",
        };
        this.storedMetrics.Add(metric);

        var controller = new TrackerMetricsController(this.metricService);
        var result = controller.GetAll();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<TrackerMetricResource>>().Subject;
        list.Should().HaveCount(1);
        list[0].TrackerUrl.Should().Be("udp://controller.tracker.org:1337/announce");
        list[0].AnnounceSuccessRate.Should().Be(100.0);
    }

    [Test]
    public void TrackerMetricsController_GetSummary_ReturnsOkWithSummary()
    {
        var controller = new TrackerMetricsController(this.metricService);
        var result = controller.GetSummary();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var summary = okResult.Value.Should().BeAssignableTo<TrackerMetricsSummary>().Subject;
        summary.Should().NotBeNull();
    }

    [Test]
    public void TrackerMetricsController_GetById_ReturnsOkWhenFound_NotFoundWhenMissing()
    {
        var metric = new TrackerMetric
        {
            Id = 3,
            TrackerUrl = "http://found.tracker.org:80/announce",
            Status = "Working",
        };
        this.storedMetrics.Add(metric);

        var controller = new TrackerMetricsController(this.metricService);

        var foundResult = controller.Get(3);
        foundResult.Result.Should().BeOfType<OkObjectResult>();

        var notFoundResult = controller.Get(99);
        notFoundResult.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void TrackerMetricsController_GetHistory_ValidatesHoursAndLimit()
    {
        var controller = new TrackerMetricsController(this.metricService);

        controller.GetHistory(1, hours: 0, limit: 100).Result.Should().BeOfType<BadRequestObjectResult>();
        controller.GetHistory(1, hours: 200, limit: 100).Result.Should().BeOfType<BadRequestObjectResult>();
        controller.GetHistory(1, hours: 24, limit: 0).Result.Should().BeOfType<BadRequestObjectResult>();
        controller.GetHistory(1, hours: 24, limit: 3000).Result.Should().BeOfType<BadRequestObjectResult>();

        var validResult = controller.GetHistory(1, hours: 24, limit: 100);
        validResult.Result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public void TrackerMetricsController_Reset_And_Delete_ReturnsOk()
    {
        var controller = new TrackerMetricsController(this.metricService);

        var resetResult = controller.Reset(1);
        resetResult.Should().BeOfType<OkObjectResult>();

        var deleteResult = controller.Delete(1);
        deleteResult.Should().BeOfType<OkObjectResult>();
    }
}
