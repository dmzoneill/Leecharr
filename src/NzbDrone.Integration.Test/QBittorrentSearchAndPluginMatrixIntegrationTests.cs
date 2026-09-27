// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class QBittorrentSearchAndPluginMatrixIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task QBittorrentSearch_PluginsAndCategories_ReturnAvailableOptions()
    {
        // 1. GET /api/v2/search/plugins
        var pluginsResp = await this.Client.GetAsync("/api/v2/search/plugins");
        pluginsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pluginsDoc = JsonDocument.Parse(await pluginsResp.Content.ReadAsStringAsync());
        pluginsDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // 2. GET /api/v2/search/categories
        var catResp = await this.Client.GetAsync("/api/v2/search/categories");
        catResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var catDoc = JsonDocument.Parse(await catResp.Content.ReadAsStringAsync());
        catDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task QBittorrentSearch_FullJobLifecycle_Succeeds()
    {
        // 1. Start search: POST /api/v2/search/start
        var startResp = await this.Client.PostAsync("/api/v2/search/start", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "pattern", "Ubuntu 24.04" },
            { "plugins", "all" },
            { "category", "all" },
        }));
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var startDoc = JsonDocument.Parse(await startResp.Content.ReadAsStringAsync());
        var searchId = startDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 2. Search status: POST /api/v2/search/status
            var statusResp = await this.Client.PostAsync("/api/v2/search/status", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", searchId.ToString() },
            }));
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Search results: POST /api/v2/search/results
            var resultsResp = await this.Client.PostAsync("/api/v2/search/results", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", searchId.ToString() },
                { "limit", "20" },
                { "offset", "0" },
            }));
            resultsResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var resultsDoc = JsonDocument.Parse(await resultsResp.Content.ReadAsStringAsync());
            resultsDoc.RootElement.TryGetProperty("results", out _).Should().BeTrue();

            // 4. Stop search: POST /api/v2/search/stop
            var stopResp = await this.Client.PostAsync("/api/v2/search/stop", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", searchId.ToString() },
            }));
            stopResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 5. Delete search: POST /api/v2/search/delete
            var delResp = await this.Client.PostAsync("/api/v2/search/delete", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", searchId.ToString() },
            }));
            delResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
