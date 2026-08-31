// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ConfigControllerTests : IntegrationTestBase
{
    [TestCase("general")]
    [TestCase("seeding")]
    [TestCase("network")]
    [TestCase("bittorrent")]
    [TestCase("peerprotocol")]
    [TestCase("protocols")]
    [TestCase("simulation")]
    [TestCase("trackerserver")]
    [TestCase("scheduler")]
    [TestCase("advanced")]
    public async Task GetConfig_returns_200_with_id1(string section)
    {
        var response = await this.Client.GetAsync($"/api/v1/config/{section}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var resource = Deserialize<Dictionary<string, object>>(json);

        resource.Should().ContainKey("id");
        resource["id"].ToString().Should().Be("1");
    }

    [Test]
    public async Task PutAdvancedConfig_returns_202()
    {
        var body = new { id = 1, uiRefreshRateSec = 99 };
        var response = await this.PutJsonAsync("/api/v1/config/advanced/1", body);
        var content = await response.Content.ReadAsStringAsync();
        TestContext.WriteLine($"RESPONSE: {response.StatusCode} -> {content}");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task PutAdvancedConfig_persists_uiRefreshRateSec()
    {
        var body = new { id = 1, uiRefreshRateSec = 42 };
        var putResponse = await this.PutJsonAsync("/api/v1/config/advanced/1", body);
        putResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var getResponse = await this.Client.GetAsync("/api/v1/config/advanced");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await getResponse.Content.ReadAsStringAsync();
        var resource = Deserialize<Dictionary<string, object>>(json);

        resource["uiRefreshRateSec"].ToString().Should().Be("42");
    }

    [Test]
    public async Task PutNetworkConfig_with_invalid_port_returns_400()
    {
        var body = new
        {
            id = 1,
            listeningPort = 0,
            maxGlobalConnections = 200,
            maxPerTorrentConnections = 50,
            maxUploadSlots = 4,
            proxyPort = 8080,
        };

        var response = await this.PutJsonAsync("/api/v1/config/network/1", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
