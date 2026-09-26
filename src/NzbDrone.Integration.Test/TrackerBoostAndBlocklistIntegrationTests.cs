// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TrackerBoostAndBlocklistIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task TrackerBoost_GetStatus_And_Settings_ReturnsSuccess()
    {
        var statusResp = await this.GetAsync("/api/v1/trackerboost/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = await statusResp.Content.ReadAsStringAsync();
        using var statusDoc = JsonDocument.Parse(statusJson);
        statusDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        var settingsResp = await this.GetAsync("/api/v1/trackerboost/settings");
        settingsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var settingsJson = await settingsResp.Content.ReadAsStringAsync();
        using var settingsDoc = JsonDocument.Parse(settingsJson);
        settingsDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Test]
    public async Task TrackerBoost_GetMatrix_And_Logs_ReturnsSuccess()
    {
        var statsResp = await this.GetAsync("/api/v1/trackerboost/matrix");
        statsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var logsResp = await this.GetAsync("/api/v1/trackerboost/logs?limit=10");
        logsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var logsJson = await logsResp.Content.ReadAsStringAsync();
        using var logsDoc = JsonDocument.Parse(logsJson);
        logsDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task EmbeddedTracker_AnnounceAndScrape_ReturnsBencodedOrValidResponse()
    {
        // Tracker stats endpoint
        var statsResp = await this.GetAsync("/api/v1/trackerserver/stats");
        statsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statsJson = await statsResp.Content.ReadAsStringAsync();
        using var statsDoc = JsonDocument.Parse(statsJson);
        statsDoc.RootElement.TryGetProperty("activeSwarms", out _).Should().BeTrue();

        // Tracker scrape endpoint
        var scrapeResp = await this.GetAsync("/scrape");
        scrapeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Tracker announce endpoint
        var announceResp = await this.GetAsync("/announce?info_hash=12345678901234567890&peer_id=ABCDEFGHIJKLMNOPQRST&port=6881&uploaded=0&downloaded=0&left=0");
        announceResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Blocklist_GetStatus_And_CheckIp_ReturnsSuccess()
    {
        var getResp = await this.GetAsync("/api/v1/blocklist");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getJson = await getResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(getJson);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        // Trigger sync
        var syncResp = await this.PostJsonAsync("/api/v1/blocklist/sync", new { });
        syncResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
