// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Trackers.Metrics;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TrackerBoostAndMetricsLifecycleIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task TrackerBoost_StatusSettingsAndHarvest_EndpointsSucceed()
    {
        // 1. GET /api/v1/trackerboost/status
        var statusResp = await this.Client.GetAsync("/api/v1/trackerboost/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /api/v1/trackerboost/settings
        var settingsResp = await this.Client.GetAsync("/api/v1/trackerboost/settings");
        settingsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. PUT /api/v1/trackerboost/settings
        var updateSettingsResp = await this.PutJsonAsync("/api/v1/trackerboost/settings", new
        {
            enabled = true,
            autoBoostNewTorrents = true,
            maxTrackersPerTorrent = 15,
        });
        updateSettingsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. GET /api/v1/trackerboost/trackers & matrix
        var trackersResp = await this.Client.GetAsync("/api/v1/trackerboost/trackers");
        trackersResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var matrixResp = await this.Client.GetAsync("/api/v1/trackerboost/matrix");
        matrixResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. POST /api/v1/trackerboost/trackers (Add manual tracker)
        var addTrackerResp = await this.PostJsonAsync("/api/v1/trackerboost/trackers", new
        {
            url = "http://tracker.boost.example.com/announce",
        });
        addTrackerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. POST /api/v1/trackerboost/trackers/bulk
        var bulkResp = await this.PostJsonAsync("/api/v1/trackerboost/trackers/bulk", new
        {
            trackersText = "http://tracker1.open.org:6969/announce\nudp://tracker2.open.org:1337/announce",
        });
        bulkResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Harvest endpoints
        var harvestDlResp = await this.PostJsonAsync("/api/v1/trackerboost/harvest/downloads", new { });
        harvestDlResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var harvestProwlarrResp = await this.PostJsonAsync("/api/v1/trackerboost/harvest/prowlarr", new { });
        harvestProwlarrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var harvestFeedsResp = await this.PostJsonAsync("/api/v1/trackerboost/harvest/feeds", new { });
        harvestFeedsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. Boost-all & Scan
        var boostAllResp = await this.PostJsonAsync("/api/v1/trackerboost/boost-all", new { });
        boostAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var scanResp = await this.PostJsonAsync("/api/v1/trackerboost/scan", new { });
        scanResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 9. Logs GET & DELETE
        var logsResp = await this.Client.GetAsync("/api/v1/trackerboost/logs");
        logsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var delLogsResp = await this.Client.DeleteAsync("/api/v1/trackerboost/logs");
        delLogsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TrackerMetrics_SummaryPruneAndLifecycle_EndpointsSucceed()
    {
        // 1. GET /api/v1/trackermetrics
        var metricsResp = await this.Client.GetAsync("/api/v1/trackermetrics");
        metricsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /api/v1/trackermetrics/summary
        var summaryResp = await this.Client.GetAsync("/api/v1/trackermetrics/summary");
        summaryResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TrackerMetrics_DetailHistoryResetDelete_EndpointsSucceed()
    {
        // 1. First record an announce to ensure at least one metric exists
        var services = GlobalSetup.Factory.Services;
        var trackerMetricService = services.GetService(typeof(ITrackerMetricService)) as ITrackerMetricService;
        trackerMetricService.Should().NotBeNull();

        var testTrackerUrl = "http://tracker.integration-endpoint-test.org:6969/announce";
        var recorded = trackerMetricService.RecordAnnounce(
            testTrackerUrl,
            torrentId: 42,
            uploaded: 1048576,
            downloaded: 2097152,
            left: 5242880,
            responseTimeMs: 85,
            success: true,
            seeders: 50,
            leechers: 20,
            peersCount: 70);
        recorded.Should().NotBeNull();
        await trackerMetricService.FlushAsync();

        // 2. GET /api/v1/trackermetrics/{id}
        var getResp = await this.Client.GetAsync($"/api/v1/trackermetrics/{recorded.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getDoc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
        getDoc.RootElement.GetProperty("trackerUrl").GetString().Should().Be(testTrackerUrl);

        // 3. GET /api/v1/trackermetrics/{id}/history valid and validations
        var historyResp = await this.Client.GetAsync($"/api/v1/trackermetrics/{recorded.Id}/history?hours=24&limit=50");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Validation: hours <= 0 -> 400
        var badHoursResp = await this.Client.GetAsync($"/api/v1/trackermetrics/{recorded.Id}/history?hours=0");
        badHoursResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Validation: hours > 168 -> 400
        var excessHoursResp = await this.Client.GetAsync($"/api/v1/trackermetrics/{recorded.Id}/history?hours=200");
        excessHoursResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Validation: limit <= 0 -> 400
        var badLimitResp = await this.Client.GetAsync($"/api/v1/trackermetrics/{recorded.Id}/history?limit=0");
        badLimitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Validation: limit > 2000 -> 400
        var excessLimitResp = await this.Client.GetAsync($"/api/v1/trackermetrics/{recorded.Id}/history?limit=5000");
        excessLimitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. POST /api/v1/trackermetrics/{id}/reset
        var resetResp = await this.PostJsonAsync($"/api/v1/trackermetrics/{recorded.Id}/reset", new { });
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. DELETE /api/v1/trackermetrics/{id}
        var delResp = await this.DeleteAsync($"/api/v1/trackermetrics/{recorded.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Non-existent metric returns 404
        var notFoundResp = await this.Client.GetAsync("/api/v1/trackermetrics/999999");
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TrackerBoost_HashInspectionAndInjection_EndpointsSucceed()
    {
        var testHash = "t1t1t1t1t1t1t1t1t1t1t1t1t1t1t1t1t1t1t1t1";

        // 1. GET /api/v1/trackerboost/check-hash/{infoHash}
        var checkHashResp = await this.Client.GetAsync($"/api/v1/trackerboost/check-hash/{testHash}?name=TestTorrent");
        checkHashResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/trackerboost/boost-hash/{infoHash}
        var boostHashResp = await this.PostJsonAsync($"/api/v1/trackerboost/boost-hash/{testHash}?name=TestTorrent&onlyVerified=false", new { });
        boostHashResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. POST /api/v1/trackerboost/inject with missing data returns BadRequest
        var badInjectResp = await this.PostJsonAsync("/api/v1/trackerboost/inject", new
        {
            trackerUrl = string.Empty,
        });
        badInjectResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. POST /api/v1/trackerboost/inject with infoHash
        var validInjectResp = await this.PostJsonAsync("/api/v1/trackerboost/inject", new
        {
            infoHash = testHash,
            trackerUrl = "http://tracker.inject.example.com:80/announce",
            force = true,
        });
        validInjectResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TrackerMetricsService_DirectLifecycleAndCalculation_Succeeds()
    {
        var services = GlobalSetup.Factory.Services;
        var trackerMetricService = services.GetService(typeof(ITrackerMetricService)) as ITrackerMetricService;
        trackerMetricService.Should().NotBeNull();

        var testUrl = "udp://tracker.direct-lifecycle-test.org:1337/announce";

        // 1. Record successful announce
        var metric = trackerMetricService.RecordAnnounce(
            testUrl,
            torrentId: 99,
            uploaded: 5000000,
            downloaded: 10000000,
            left: 2000000,
            responseTimeMs: 45,
            success: true,
            seeders: 100,
            leechers: 25,
            peersCount: 125);
        metric.Should().NotBeNull();
        metric.Status.Should().Be("Working");
        metric.SuccessfulAnnounces.Should().BeGreaterThan(0);

        // 2. Record scrape
        var scrapeMetric = trackerMetricService.RecordScrape(
            testUrl,
            responseTimeMs: 30,
            success: true,
            seeders: 110,
            leechers: 20,
            completed: 500);
        scrapeMetric.Should().NotBeNull();
        scrapeMetric.SuccessfulScrapes.Should().BeGreaterThan(0);

        // 3. Consecutive failures trigger Degraded and Offline
        for (var i = 0; i < 2; i++)
        {
            trackerMetricService.RecordAnnounce(
                testUrl,
                torrentId: 99,
                uploaded: 0,
                downloaded: 0,
                left: 0,
                responseTimeMs: 0,
                success: false,
                seeders: 0,
                leechers: 0,
                peersCount: 0,
                error: "Connection timed out");
        }
        var degradedMetric = trackerMetricService.GetMetricByUrl(testUrl);
        degradedMetric.Status.Should().Be("Degraded");

        for (var i = 0; i < 3; i++)
        {
            trackerMetricService.RecordAnnounce(
                testUrl,
                torrentId: 99,
                uploaded: 0,
                downloaded: 0,
                left: 0,
                responseTimeMs: 0,
                success: false,
                seeders: 0,
                leechers: 0,
                peersCount: 0,
                error: "Host unreachable");
        }
        var offlineMetric = trackerMetricService.GetMetricByUrl(testUrl);
        offlineMetric.Status.Should().Be("Offline");

        // 4. Scrape failure
        trackerMetricService.RecordScrape(
            testUrl,
            responseTimeMs: 0,
            success: false,
            seeders: 0,
            leechers: 0,
            completed: 0,
            error: "Scrape connection refused");

        // 5. SeedFromExistingTrackers, FlushAsync, and PruneSnapshots
        trackerMetricService.SeedFromExistingTrackers();
        await trackerMetricService.FlushAsync();
        trackerMetricService.PruneSnapshots(DateTime.UtcNow.AddDays(-7));

        // 6. Summary metrics
        var summary = trackerMetricService.GetSummary();
        summary.Should().NotBeNull();
        summary.TotalTrackers.Should().BeGreaterThan(0);
        summary.GlobalRatio.Should().BeGreaterThanOrEqualTo(0.0);
        summary.AnnounceSuccessRate.Should().BeGreaterThanOrEqualTo(0.0);
        summary.TotalScrapes.Should().BeGreaterThan(0);
    }
}
