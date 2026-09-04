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
    public async Task ProbeEngine_via_config_engine_routes_returns_200()
    {
        // 1. POST /api/v1/config/engine/probe (active engine default)
        var responseDefault = await this.Client.PostAsync("/api/v1/config/engine/probe", null);
        responseDefault.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonDefault = await responseDefault.Content.ReadAsStringAsync();
        var probeDefault = Deserialize<JsonElement>(jsonDefault);
        probeDefault.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
        probeDefault.GetProperty("engineId").GetString().Should().NotBeNullOrEmpty();
        probeDefault.GetProperty("statusMessage").GetString().Should().NotBeNullOrEmpty();

        // 2. POST /api/v1/config/engine/probe?engineId=MonoTorrent
        var responseQuery = await this.Client.PostAsync("/api/v1/config/engine/probe?engineId=MonoTorrent", null);
        responseQuery.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonQuery = await responseQuery.Content.ReadAsStringAsync();
        var probeQuery = Deserialize<JsonElement>(jsonQuery);
        probeQuery.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
        probeQuery.GetProperty("engineId").GetString().Should().Be("MonoTorrent");

        // 3. POST /api/v1/config/engine/probe with JSON body
        var responseBody = await this.PostJsonAsync("/api/v1/config/engine/probe", new { engineId = "MonoTorrent" });
        responseBody.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonBody = await responseBody.Content.ReadAsStringAsync();
        var probeBody = Deserialize<JsonElement>(jsonBody);
        probeBody.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
        probeBody.GetProperty("engineId").GetString().Should().Be("MonoTorrent");

        // 4. POST /api/v1/config/engine/MonoTorrent/probe
        var responsePath = await this.Client.PostAsync("/api/v1/config/engine/MonoTorrent/probe", null);
        responsePath.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonPath = await responsePath.Content.ReadAsStringAsync();
        var probePath = Deserialize<JsonElement>(jsonPath);
        probePath.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
        probePath.GetProperty("engineId").GetString().Should().Be("MonoTorrent");

        // 5. GET /api/v1/config/engine/probe
        var responseGet = await this.Client.GetAsync("/api/v1/config/engine/probe?engineId=MonoTorrent");
        responseGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonGet = await responseGet.Content.ReadAsStringAsync();
        var probeGet = Deserialize<JsonElement>(jsonGet);
        probeGet.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
        probeGet.GetProperty("engineId").GetString().Should().Be("MonoTorrent");
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
