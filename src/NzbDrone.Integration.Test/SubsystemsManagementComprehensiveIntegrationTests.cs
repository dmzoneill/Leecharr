// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SubsystemsManagementComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Subsystems_GetAllAndGetById_ReturnsNineSubsystems()
    {
        // 1. GET /api/v1/subsystems - Returns all 9 subsystems
        var allResp = await this.Client.GetAsync("/api/v1/subsystems");
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allJson = await allResp.Content.ReadAsStringAsync();
        using var allDoc = JsonDocument.Parse(allJson);
        allDoc.RootElement.GetArrayLength().Should().Be(9);

        // 2. GET /api/v1/subsystems/{id} for each subsystem
        var subsystemIds = new[]
        {
            "bittorrent", "extractor", "mediainspector", "geoip", "blocklist",
            "networkbinding", "mediametadata", "httptransport", "ai",
        };

        foreach (var id in subsystemIds)
        {
            var getResp = await this.Client.GetAsync($"/api/v1/subsystems/{id}");
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getJson = await getResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(getJson);
            doc.RootElement.GetProperty("id").GetString().Should().Be(id);
            doc.RootElement.GetProperty("providers").GetArrayLength().Should().BeGreaterThan(0);
        }

        // 3. GET /api/v1/subsystems/unknown -> 404
        var notFoundResp = await this.Client.GetAsync("/api/v1/subsystems/unknown_subsystem_123");
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Subsystems_MetricsAndBlocklistUpdate_OperateSuccessfully()
    {
        // 1. GET /api/v1/subsystems/metrics
        var metricsResp = await this.Client.GetAsync("/api/v1/subsystems/metrics");
        metricsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /api/v1/subsystems/{id}/metrics
        var bitTorrentMetricsResp = await this.Client.GetAsync("/api/v1/subsystems/bittorrent/metrics");
        bitTorrentMetricsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. POST /api/v1/subsystems/blocklist/update
        var updateResp = await this.Client.PostAsync("/api/v1/subsystems/blocklist/update", null);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateJson = await updateResp.Content.ReadAsStringAsync();
        using var updateDoc = JsonDocument.Parse(updateJson);
        updateDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task Subsystems_ProbeAndSwitch_HandleValidAndInvalidProviders()
    {
        // 1. POST /api/v1/subsystems/ai/probe/RuleHeuristic
        var probeResp = await this.Client.PostAsync("/api/v1/subsystems/ai/probe/RuleHeuristic", null);
        probeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var probeJson = await probeResp.Content.ReadAsStringAsync();
        using var probeDoc = JsonDocument.Parse(probeJson);
        probeDoc.RootElement.GetProperty("isHealthy").GetBoolean().Should().BeTrue();

        // 2. POST /api/v1/subsystems/ai/switch to RuleHeuristic
        var switchPayload = new
        {
            providerId = "RuleHeuristic",
        };
        var switchResp = await this.PostJsonAsync("/api/v1/subsystems/ai/switch", switchPayload);
        switchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var switchJson = await switchResp.Content.ReadAsStringAsync();
        using var switchDoc = JsonDocument.Parse(switchJson);
        switchDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        switchDoc.RootElement.GetProperty("activeProvider").GetString().Should().Be("RuleHeuristic");

        // 3. Switch with empty ProviderId -> 400 BadRequest
        var emptySwitchResp = await this.PostJsonAsync("/api/v1/subsystems/ai/switch", new { providerId = "" });
        emptySwitchResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. Switch on unknown subsystem -> 404 NotFound
        var unknownSubsystemResp = await this.PostJsonAsync("/api/v1/subsystems/unknown/switch", new { providerId = "test" });
        unknownSubsystemResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
