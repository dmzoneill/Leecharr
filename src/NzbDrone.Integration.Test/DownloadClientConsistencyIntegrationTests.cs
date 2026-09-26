// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Torrents;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DownloadClientConsistencyIntegrationTests : IntegrationTestBase
{
    private const string TestHash = "0123456789abcdef0123456789abcdef01234569";
    private const string TestName = "ConsistencyVerificationTorrent";
    private const string TestCategory = "tv-sonarr";

    [Test]
    public async Task AllDownloadClientInterfaces_ReturnConsistentTorrentInformation()
    {
        // 1. Add test torrent to Leecharr
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent($"magnet:?xt=urn:btih:{TestHash}&dn={TestName}&tr=https://tracker.example.com/announce"), "magnetUrl");
        form.Add(new StringContent(TestCategory), "category");
        form.Add(new StringContent("true"), "paused");

        var addResponse = await this.Client.PostAsync("/api/v1/torrents", form);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = Deserialize<TorrentResource>(await addResponse.Content.ReadAsStringAsync());
        created.Id.Should().BeGreaterThan(0);

        try
        {
            // 2. Query Leecharr Native REST API
            var leecharrTorrents = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
            var leecharrTorrent = leecharrTorrents.FirstOrDefault(t => string.Equals(t.InfoHash, TestHash, StringComparison.OrdinalIgnoreCase));
            leecharrTorrent.Should().NotBeNull();
            leecharrTorrent!.Name.Should().Be(TestName);
            leecharrTorrent.Category.Should().Be(TestCategory);

            var expectedDownloadDir = leecharrTorrent.SavePath;

            // 3. Query Transmission RPC Emulation
            var transInit = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
            transInit.Headers.TryGetValues("X-Transmission-Session-Id", out var sessionIds).Should().BeTrue();
            var sessionId = sessionIds!.FirstOrDefault();

            var transReq = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        method = "torrent-get",
                        arguments = new
                        {
                            fields = new[] { "hashString", "name", "totalSize", "status", "downloadDir", "labels" },
                        },
                    }),
                    Encoding.UTF8,
                    "application/json"),
            };
            transReq.Headers.Add("X-Transmission-Session-Id", sessionId);

            var transResp = await this.Client.SendAsync(transReq);
            transResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var transDoc = JsonDocument.Parse(await transResp.Content.ReadAsStringAsync());
            var transTorrents = transDoc.RootElement.GetProperty("arguments").GetProperty("torrents");

            JsonElement matchingTrans = default;
            var foundTrans = false;
            foreach (var t in transTorrents.EnumerateArray())
            {
                if (string.Equals(t.GetProperty("hashString").GetString(), TestHash, StringComparison.OrdinalIgnoreCase))
                {
                    matchingTrans = t;
                    foundTrans = true;
                    break;
                }
            }

            foundTrans.Should().BeTrue("Transmission RPC should return the added torrent");
            matchingTrans.GetProperty("name").GetString().Should().Be(TestName);
            matchingTrans.GetProperty("downloadDir").GetString().Should().Be(expectedDownloadDir);
            var transLabels = matchingTrans.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ToList();
            transLabels.Should().Contain(TestCategory);

            // 4. Query qBittorrent Web API Emulation
            var qbitLogin = await this.Client.PostAsync(
                "/api/v2/auth/login",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", "admin"),
                    new KeyValuePair<string, string>("password", "adminadmin"),
                }));
            qbitLogin.StatusCode.Should().Be(HttpStatusCode.OK);

            var qbitResp = await this.GetAsync("/api/v2/torrents/info");
            qbitResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var qbitDoc = JsonDocument.Parse(await qbitResp.Content.ReadAsStringAsync());

            JsonElement matchingQbit = default;
            var foundQbit = false;
            foreach (var t in qbitDoc.RootElement.EnumerateArray())
            {
                if (string.Equals(t.GetProperty("hash").GetString(), TestHash, StringComparison.OrdinalIgnoreCase))
                {
                    matchingQbit = t;
                    foundQbit = true;
                    break;
                }
            }

            foundQbit.Should().BeTrue("qBittorrent API should return the added torrent");
            matchingQbit.GetProperty("name").GetString().Should().Be(TestName);
            matchingQbit.GetProperty("category").GetString().Should().Be(TestCategory);
            matchingQbit.GetProperty("save_path").GetString().Should().Be(expectedDownloadDir);

            // 5. Query Deluge JSON-RPC Emulation
            var delugeReq = new
            {
                id = 1,
                method = "core.get_torrents_status",
                @params = new object[]
                {
                    new { },
                    new[] { "name", "hash", "total_size", "state", "save_path", "label" },
                },
            };

            var delugeResp = await this.PostJsonAsync("/json", delugeReq);
            delugeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var delugeDoc = JsonDocument.Parse(await delugeResp.Content.ReadAsStringAsync());
            var delugeResult = delugeDoc.RootElement.GetProperty("result");

            delugeResult.TryGetProperty(TestHash.ToLowerInvariant(), out var matchingDeluge).Should().BeTrue("Deluge JSON-RPC should return the added torrent");
            matchingDeluge.GetProperty("name").GetString().Should().Be(TestName);
            matchingDeluge.GetProperty("save_path").GetString().Should().Be(expectedDownloadDir);

            // 6. Test Tracker Addition Consistency
            var newTracker = "https://tracker2.example.com/announce";

            // Add via qBittorrent API
            var qbitAddTracker = await this.Client.PostAsync(
                "/api/v2/torrents/addTrackers",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hash", TestHash),
                    new KeyValuePair<string, string>("urls", newTracker),
                }));
            qbitAddTracker.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verify tracker is returned across Leecharr REST and qBittorrent
            var qbitTrackersResp = await this.GetAsync($"/api/v2/torrents/trackers?hash={TestHash}");
            qbitTrackersResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var qbitTrackersDoc = JsonDocument.Parse(await qbitTrackersResp.Content.ReadAsStringAsync());
            var qbitUrls = qbitTrackersDoc.RootElement.EnumerateArray().Select(tr => tr.GetProperty("url").GetString()).ToList();
            qbitUrls.Should().Contain(newTracker);

            // 7. Test Reannounce Across Interfaces
            var qbitReannounce = await this.Client.PostAsync(
                "/api/v2/torrents/reannounce",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", TestHash),
                }));
            qbitReannounce.StatusCode.Should().Be(HttpStatusCode.OK);

            var delugeReannounce = await this.PostJsonAsync(
                "/json",
                new
                {
                    id = 2,
                    method = "core.force_reannounce",
                    @params = new object[] { new[] { TestHash } },
                });
            delugeReannounce.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // Clean up
            await this.DeleteAsync($"/api/v1/torrents/{created.Id}?deleteFiles=false");
        }
    }
}
