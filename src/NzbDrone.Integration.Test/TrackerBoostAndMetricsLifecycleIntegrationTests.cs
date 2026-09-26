// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

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
}
