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
public class TorrentManagementDeepEndpointsIntegrationTests : IntegrationTestBase
{
    private const string DeepHash = "d777777777777777777777777777777777777777";
    private const string DeepName = "TorrentDeepMovie";

    [Test]
    public async Task Torrent_BulkActionsMatrix_ExecutesSuccessfully()
    {
        // 1. Add two torrents to bulk manage
        var addResp1 = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{DeepHash}&dn={DeepName}1",
            category = "initial",
            paused = true,
        });
        addResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var id1 = JsonDocument.Parse(await addResp1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        var hash2 = "d888888888888888888888888888888888888888";
        var addResp2 = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{hash2}&dn={DeepName}2",
            category = "initial",
            paused = true,
        });
        addResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var id2 = JsonDocument.Parse(await addResp2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        try
        {
            var ids = new List<int> { id1, id2 };

            // 2. Bulk pause & resume
            var pauseResp = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "pause",
                torrentIds = ids,
            });
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var resumeResp = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "resume",
                torrentIds = ids,
            });
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Bulk recheck & announce
            var recheckResp = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "recheck",
                torrentIds = ids,
            });
            recheckResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var announceResp = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "announce",
                torrentIds = ids,
            });
            announceResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Bulk setCategory
            var catResp = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "setcategory",
                torrentIds = ids,
                category = "bulk-category",
            });
            catResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Bulk setLimits
            var limitsResp = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "setlimits",
                torrentIds = ids,
                downloadLimit = 5000,
                uploadLimit = 1000,
                ratioLimit = 2.0,
                seedingTimeLimit = 60,
            });
            limitsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Bulk invalid / empty action
            var emptyResp = await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "",
                torrentIds = ids,
            });
            emptyResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            // 7. Bulk delete
            await this.PostJsonAsync("/api/v1/torrents/bulk", new
            {
                action = "delete",
                torrentIds = new List<int> { id1, id2 },
                deleteFiles = false,
            });
        }
    }

    [Test]
    public async Task Torrent_TrackersAndPeersAndStream_EndpointsSucceed()
    {
        // Add a torrent
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{DeepHash}&dn={DeepName}",
            category = "stream-test",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var torrentId = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        try
        {
            // 1. Trackers: GET, POST, announce
            var getTrackersResp = await this.Client.GetAsync($"/api/v1/torrents/{torrentId}/trackers");
            getTrackersResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var addTrackerResp = await this.PostJsonAsync($"/api/v1/torrents/{torrentId}/trackers", new
            {
                url = "http://tracker.deep.example.com:6969/announce",
            });
            addTrackerResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var postAnnounceResp = await this.PostJsonAsync($"/api/v1/torrents/{torrentId}/announce", new { });
            postAnnounceResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. Peers: GET, ban, unban
            var getPeersResp = await this.Client.GetAsync($"/api/v1/torrents/{torrentId}/peers");
            getPeersResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var banPeerResp = await this.PostJsonAsync($"/api/v1/torrents/{torrentId}/peers/ban", new
            {
                ip = "192.168.1.200",
            });
            banPeerResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var unbanPeerResp = await this.Client.DeleteAsync($"/api/v1/torrents/{torrentId}/peers/192.168.1.200");
            unbanPeerResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Queue movement: PUT /queue
            var queueResp = await this.PutJsonAsync($"/api/v1/torrents/{torrentId}/queue", new
            {
                position = "top",
            });
            queueResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Stream playlist (returns 404 when no files downloaded yet) and subtitles
            var playlistResp = await this.Client.GetAsync($"/api/v1/torrents/{torrentId}/stream.m3u");
            playlistResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var subsResp = await this.Client.GetAsync($"/api/v1/torrents/{torrentId}/subtitles");
            subsResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // 5. Torrent activity logs: GET /logs
            var logsResp = await this.Client.GetAsync($"/api/v1/torrents/{torrentId}/logs");
            logsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }
}
