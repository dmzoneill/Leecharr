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
            // 8. RTorrent XML-RPC d.get_directory
            var rtorrentXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_directory</methodName><params><param><value><string>{TestHash}</string></value></param></params></methodCall>";
            var rtorrentContent = new StringContent(rtorrentXml, Encoding.UTF8, "text/xml");
            var rtorrentResp = await this.Client.PostAsync("/RPC2", rtorrentContent);
            rtorrentResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var rtorrentResXml = await rtorrentResp.Content.ReadAsStringAsync();
            rtorrentResXml.Should().Contain(expectedDownloadDir);

            // 9. Aria2 JSON-RPC aria2.tellStatus
            var aria2Gid = TestHash[..16];
            var aria2Req = new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "aria2.tellStatus",
                @params = new object[] { aria2Gid },
            };
            var aria2Resp = await this.PostJsonAsync("/jsonrpc", aria2Req);
            aria2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var aria2Doc = JsonDocument.Parse(await aria2Resp.Content.ReadAsStringAsync());
            var aria2Dir = aria2Doc.RootElement.GetProperty("result").GetProperty("dir").GetString();
            aria2Dir.Should().Be(expectedDownloadDir);

            // 10. UTorrent WebUI /gui/?list=1
            var utorrentResp = await this.Client.GetAsync("/gui/?list=1");
            utorrentResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var utorrentDoc = JsonDocument.Parse(await utorrentResp.Content.ReadAsStringAsync());
            var utorrentList = utorrentDoc.RootElement.GetProperty("torrents");
            var foundUtorrent = false;
            foreach (var row in utorrentList.EnumerateArray())
            {
                if (string.Equals(row[0].GetString(), TestHash, StringComparison.OrdinalIgnoreCase))
                {
                    row[26].GetString().Should().Be(expectedDownloadDir);
                    foundUtorrent = true;
                    break;
                }
            }
            foundUtorrent.Should().BeTrue("uTorrent WebUI should return the added torrent");

            // 11. Flood API /api/torrents
            var floodResp = await this.Client.GetAsync("/api/torrents");
            floodResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var floodDoc = JsonDocument.Parse(await floodResp.Content.ReadAsStringAsync());
            var floodTorrents = floodDoc.RootElement.GetProperty("torrents");
            floodTorrents.TryGetProperty(TestHash.ToLowerInvariant(), out var floodTorrent)
                .Should().BeTrue("Flood API should return the added torrent");
            floodTorrent.GetProperty("directory").GetString().Should().Be(expectedDownloadDir);

            // 12. Hadouken JSON-RPC webui.list
            var hadoukenReq = new
            {
                id = 4,
                method = "webui.list",
            };
            var hadoukenResp = await this.PostJsonAsync("/api/hadouken", hadoukenReq);
            hadoukenResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var hadoukenDoc = JsonDocument.Parse(await hadoukenResp.Content.ReadAsStringAsync());
            var hadoukenTorrents = hadoukenDoc.RootElement.GetProperty("result").GetProperty("torrents");
            var foundHadouken = false;
            foreach (var row in hadoukenTorrents.EnumerateArray())
            {
                if (string.Equals(row[0].GetString(), TestHash, StringComparison.OrdinalIgnoreCase))
                {
                    row[26].GetString().Should().Be(expectedDownloadDir);
                    foundHadouken = true;
                    break;
                }
            }
            foundHadouken.Should().BeTrue("Hadouken RPC should return the added torrent");

            // 13. Nzbget JSON-RPC listgroups
            var nzbgetReq = new
            {
                id = 5,
                method = "listgroups",
                @params = Array.Empty<object>(),
            };
            var nzbgetResp = await this.PostJsonAsync("/nzbget/jsonrpc", nzbgetReq);
            nzbgetResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var nzbgetDoc = JsonDocument.Parse(await nzbgetResp.Content.ReadAsStringAsync());
            var nzbgetGroups = nzbgetDoc.RootElement.GetProperty("result");
            var foundNzbget = false;
            foreach (var g in nzbgetGroups.EnumerateArray())
            {
                var idProp = g.TryGetProperty("nzbid", out var nId) ? nId.GetInt32() : (g.TryGetProperty("NZBID", out var nId2) ? nId2.GetInt32() : -1);
                if (idProp == created.Id)
                {
                    var destDirProp = g.TryGetProperty("destDir", out var dd) ? dd.GetString() : g.GetProperty("DestDir").GetString();
                    destDirProp.Should().Be(expectedDownloadDir);
                    foundNzbget = true;
                    break;
                }
            }
            foundNzbget.Should().BeTrue("Nzbget JSON-RPC should return the added item");

            // 14. Freebox API /api/v4/downloads/
            await this.Client.GetAsync("/api/v4/login/session");
            var fbResp = await this.Client.GetAsync("/api/v4/downloads/");
            fbResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var fbDoc = JsonDocument.Parse(await fbResp.Content.ReadAsStringAsync());
            var fbList = fbDoc.RootElement.GetProperty("result");
            var foundFb = false;
            foreach (var item in fbList.EnumerateArray())
            {
                if (item.GetProperty("id").GetInt32() == created.Id)
                {
                    var b64Dir = item.GetProperty("download_dir").GetString();
                    var decodedDir = Encoding.UTF8.GetString(Convert.FromBase64String(b64Dir!));
                    decodedDir.Should().Be(expectedDownloadDir);
                    foundFb = true;
                    break;
                }
            }
            foundFb.Should().BeTrue("Freebox API should return the added download");
        }
        finally
        {
            // Clean up
            await this.DeleteAsync($"/api/v1/torrents/{created.Id}?deleteFiles=false");
        }
    }
}
