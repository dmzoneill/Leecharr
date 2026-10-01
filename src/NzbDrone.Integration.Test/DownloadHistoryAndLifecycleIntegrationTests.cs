// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DownloadHistoryAndLifecycleIntegrationTests : IntegrationTestBase
{
    private const string HistoryHash = "e888888888888888888888888888888888888888";
    private const string HistoryName = "HistoryLifecycleMovie";

    [Test]
    public async Task DownloadHistory_FullLifecycleEndpoints_Succeeds()
    {
        // 1. Add a torrent to trigger TorrentAddedEvent and history recording
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{HistoryHash}&dn={HistoryName}",
            category = "movies",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 2. Query history list: GET /api/v1/downloadhistory
            var listResp = await this.Client.GetAsync("/api/v1/downloadhistory?limit=100&offset=0");
            listResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
            listDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

            // 3. Query history with search query filter
            var queryResp = await this.Client.GetAsync($"/api/v1/downloadhistory?query={HistoryName}");
            queryResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Query non-existent history ID
            var notFoundResp = await this.Client.GetAsync("/api/v1/downloadhistory/999999");
            notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // 5. Test enrich-all & reconcile endpoints
            var enrichResp = await this.PostJsonAsync("/api/v1/downloadhistory/enrich-all", new { });
            enrichResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var reconcileResp = await this.PostJsonAsync("/api/v1/downloadhistory/reconcile", new { });
            reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Test delete with non-existent id
            var delResp = await this.Client.DeleteAsync("/api/v1/downloadhistory/999999");
            delResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NoContent);

            // 7. Test readd on non-existent id
            var readdNotFound = await this.PostJsonAsync("/api/v1/downloadhistory/999999/readd", new { });
            readdNotFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }
}
