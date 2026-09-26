// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Torrents;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentControllerComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string HashA = "a111111111111111111111111111111111111111";
    private const string NameA = "TorrentLifecycleMovieA";

    [Test]
    public async Task TorrentController_FullLifecycleAndActions_AllEndpointsSucceed()
    {
        // 1. Add torrent via JSON endpoint
        var addPayload = new
        {
            magnetLink = $"magnet:?xt=urn:btih:{HashA}&dn={NameA}&tr=https://tracker.example.com/announce",
            category = "movies",
            paused = true,
            sequentialDownload = false,
            firstLastPiecePriority = false,
        };

        var addResp = await this.PostJsonAsync("/api/v1/torrents", addPayload);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addJson = await addResp.Content.ReadAsStringAsync();
        using var addDoc = JsonDocument.Parse(addJson);
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();
        torrentId.Should().BeGreaterThan(0);

        try
        {
            // 2. GET by ID
            var getResp = await this.GetAsync($"/api/v1/torrents/{torrentId}");
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getJson = await getResp.Content.ReadAsStringAsync();
            using var getDoc = JsonDocument.Parse(getJson);
            getDoc.RootElement.GetProperty("name").GetString().Should().Be(NameA);
            getDoc.RootElement.GetProperty("category").GetString().Should().Be("movies");
            getDoc.RootElement.GetProperty("status").GetString().Should().BeOneOf("stopped", "paused", "Paused");

            // 3. PUT update properties (category, priority, limits, sequential)
            var updatePayload = new
            {
                id = torrentId,
                category = "movies-4k",
                priority = 2,
                downloadLimit = 1000,
                uploadLimit = 500,
                sequentialDownload = true,
                firstLastPiecePriority = true,
                active = true,
            };

            var updateResp = await this.PutJsonAsync($"/api/v1/torrents/{torrentId}", updatePayload);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateJson = await updateResp.Content.ReadAsStringAsync();
            using var updateDoc = JsonDocument.Parse(updateJson);
            updateDoc.RootElement.GetProperty("category").GetString().Should().Be("movies-4k");
            updateDoc.RootElement.GetProperty("priority").GetInt32().Should().Be(2);

            // 4. Force Announce
            var announceResp = await this.PostJsonAsync($"/api/v1/torrents/{torrentId}/announce", new { });
            announceResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Query Trackers
            var trackersResp = await this.GetAsync($"/api/v1/torrents/{torrentId}/trackers");
            trackersResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var trackersJson = await trackersResp.Content.ReadAsStringAsync();
            using var trackersDoc = JsonDocument.Parse(trackersJson);
            trackersDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            trackersDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

            // 6. Query Peers
            var peersResp = await this.GetAsync($"/api/v1/torrents/{torrentId}/peers");
            peersResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var peersJson = await peersResp.Content.ReadAsStringAsync();
            using var peersDoc = JsonDocument.Parse(peersJson);
            peersDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

            // 7. Ban / Disconnect Peer
            var banResp = await this.PostJsonAsync($"/api/v1/torrents/{torrentId}/peers/ban", new { ip = "192.168.1.55" });
            banResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 8. Move Queue Position (up, down, top, bottom)
            var moveUpResp = await this.PostJsonAsync($"/api/v1/torrents/{torrentId}/queue", new { position = "up" });
            moveUpResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var moveTopResp = await this.PostJsonAsync($"/api/v1/torrents/{torrentId}/queue", new { position = "top" });
            moveTopResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 9. Bulk Actions (pause, resume, setCategory)
            var bulkPause = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                torrentIds = new[] { torrentId },
                action = "pause",
            });
            bulkPause.StatusCode.Should().Be(HttpStatusCode.OK);

            var bulkResume = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                torrentIds = new[] { torrentId },
                action = "resume",
            });
            bulkResume.StatusCode.Should().Be(HttpStatusCode.OK);

            var bulkCategory = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                torrentIds = new[] { torrentId },
                action = "setCategory",
                category = "tv-sonarr",
            });
            bulkCategory.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 10. Delete
            var deleteResp = await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
            deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var verifyDeleted = await this.GetAsync($"/api/v1/torrents/{torrentId}");
            verifyDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
