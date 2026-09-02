// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentEngineControllerTests : IntegrationTestBase
{
    [Test]
    public async Task GetEngines_returns_200_with_all_engines()
    {
        var response = await this.Client.GetAsync("/api/v1/torrentengine");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var engines = Deserialize<List<JsonElement>>(json);

        engines.Should().NotBeEmpty();
        engines.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task GetActiveEngine_returns_200_with_active_status()
    {
        var response = await this.Client.GetAsync("/api/v1/torrentengine/active");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var status = Deserialize<JsonElement>(json);

        status.GetProperty("engineId").GetString().Should().NotBeNullOrEmpty();
        status.GetProperty("protocolName").GetString().Should().Be("BitTorrent");
    }

    [Test]
    public async Task ProbeEngine_returns_200_with_health_checks()
    {
        var response = await this.Client.PostAsync("/api/v1/torrentengine/MonoTorrent/probe", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var probe = Deserialize<JsonElement>(json);

        probe.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
        probe.GetProperty("engineId").GetString().Should().Be("MonoTorrent");
    }

    [Test]
    public async Task SwitchEngine_to_LibTorrent_and_back_to_MonoTorrent()
    {
        // 1. Switch to LibTorrent
        var switchReq1 = new { engineId = "LibTorrent", preserveTransfers = true };
        var response1 = await this.PostJsonAsync("/api/v1/torrentengine/switch", switchReq1);
        var json1 = await response1.Content.ReadAsStringAsync();
        TestContext.WriteLine($"SWITCH 1 RESPONSE: {response1.StatusCode} -> {json1}");
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = Deserialize<JsonElement>(json1);
        result1.GetProperty("success").GetBoolean().Should().BeTrue();
        result1.GetProperty("activeEngine").GetString().Should().Be("LibTorrent");

        // 2. Verify active engine endpoint reflects LibTorrent
        var activeResponse1 = await this.Client.GetAsync("/api/v1/torrentengine/active");
        activeResponse1.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeJson1 = await activeResponse1.Content.ReadAsStringAsync();
        var active1 = Deserialize<JsonElement>(activeJson1);
        active1.GetProperty("engineId").GetString().Should().Be("LibTorrent");

        // 3. Switch back to MonoTorrent
        var switchReq2 = new { engineId = "MonoTorrent", preserveTransfers = true };
        var response2 = await this.PostJsonAsync("/api/v1/torrentengine/switch", switchReq2);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var json2 = await response2.Content.ReadAsStringAsync();
        var result2 = Deserialize<JsonElement>(json2);
        result2.GetProperty("success").GetBoolean().Should().BeTrue();
        result2.GetProperty("activeEngine").GetString().Should().Be("MonoTorrent");
    }
}
