// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TransmissionRpcTests : IntegrationTestBase
{
    [Test]
    public async Task SessionGet_WhenNoSessionId_Returns409AndGeneratesSessionHeader()
    {
        var rpcBody = new
        {
            method = "session-get",
            tag = 1,
        };

        var response = await this.PostJsonAsync("/transmission/rpc", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Headers.Should().ContainKey("X-Transmission-Session-Id");
    }

    [Test]
    public async Task SessionGet_WithSessionHeader_ReturnsSuccess()
    {
        // 1. Initial request to get session id
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        initial.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        // 2. Subsequent request with session header
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "session-get", tag = 10 }), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").GetString().Should().Be("success");
        root.GetProperty("arguments").GetProperty("version").GetString().Should().Contain("Leecharr");
    }

    [Test]
    public async Task TorrentGet_WithSessionHeader_ReturnsTorrentsList()
    {
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "torrent-get", tag = 11 }), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").GetString().Should().Be("success");
        root.GetProperty("arguments").TryGetProperty("torrents", out _).Should().BeTrue();
    }

    [Test]
    public async Task TorrentLifecycle_AddPauseResumeDelete_Succeeds()
    {
        // 1. Get session ID
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        async Task<JsonElement> SendRpcAsync(object body)
        {
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
            {
                Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Transmission-Session-Id", sessionId);

            var resp = await this.Client.SendAsync(request);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        const string hash = "0123456789abcdef0123456789abcdef0123456a";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=TransLifecycleTorrent";

        // 2. Add Torrent
        var addResult = await SendRpcAsync(new
        {
            method = "torrent-add",
            arguments = new Dictionary<string, object>
            {
                { "filename", magnet },
                { "paused", true },
            },
            tag = 20,
        });

        addResult.GetProperty("result").GetString().Should().Be("success");
        var torrentAdded = addResult.GetProperty("arguments").GetProperty("torrent-added");
        var torrentId = torrentAdded.GetProperty("id").GetInt32();
        torrentId.Should().BeGreaterThan(0);

        // 3. Start Torrent (Resume)
        var startResult = await SendRpcAsync(new
        {
            method = "torrent-start",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } },
            },
            tag = 21,
        });
        startResult.GetProperty("result").GetString().Should().Be("success");

        // 4. Stop Torrent (Pause)
        var stopResult = await SendRpcAsync(new
        {
            method = "torrent-stop",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } },
            },
            tag = 22,
        });
        stopResult.GetProperty("result").GetString().Should().Be("success");

        // 5. Remove Torrent (with local data)
        var removeResult = await SendRpcAsync(new
        {
            method = "torrent-remove",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } },
                { "delete-local-data", true },
            },
            tag = 23,
        });
        removeResult.GetProperty("result").GetString().Should().Be("success");

        // 6. Verify Torrent is gone
        var getResult = await SendRpcAsync(new
        {
            method = "torrent-get",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } },
            },
            tag = 24,
        });
        getResult.GetProperty("result").GetString().Should().Be("success");
        getResult.GetProperty("arguments").GetProperty("torrents").GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task SessionStats_WithSessionHeader_ReturnsCompleteStats()
    {
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "session-stats", tag = 30 }), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").GetString().Should().Be("success");
        var args = root.GetProperty("arguments");
        args.TryGetProperty("activeTorrentCount", out _).Should().BeTrue();
        args.TryGetProperty("pausedTorrentCount", out _).Should().BeTrue();
        args.TryGetProperty("torrentCount", out _).Should().BeTrue();
        args.TryGetProperty("downloadSpeed", out _).Should().BeTrue();
        args.TryGetProperty("uploadSpeed", out _).Should().BeTrue();

        var cumulative = args.GetProperty("cumulative-stats");
        cumulative.TryGetProperty("downloadedBytes", out _).Should().BeTrue();
        cumulative.TryGetProperty("uploadedBytes", out _).Should().BeTrue();
        cumulative.TryGetProperty("filesAdded", out _).Should().BeTrue();
        cumulative.TryGetProperty("sessionCount", out _).Should().BeTrue();
        cumulative.TryGetProperty("secondsActive", out _).Should().BeTrue();

        var current = args.GetProperty("current-stats");
        current.TryGetProperty("downloadedBytes", out _).Should().BeTrue();
        current.TryGetProperty("uploadedBytes", out _).Should().BeTrue();
        current.TryGetProperty("filesAdded", out _).Should().BeTrue();
        current.TryGetProperty("sessionCount", out _).Should().BeTrue();
        current.TryGetProperty("secondsActive", out _).Should().BeTrue();
    }

    [Test]
    public async Task BlocklistUpdate_WithSessionHeader_ReturnsBlocklistSize()
    {
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "blocklist-update", tag = 40 }), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").GetString().Should().Be("success");
        root.GetProperty("tag").GetInt32().Should().Be(40);
        root.GetProperty("arguments").TryGetProperty("blocklist-size", out _).Should().BeTrue();
    }

    [Test]
    public async Task SessionClose_WithSessionHeader_ReturnsSuccess()
    {
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "session-close", tag = 41 }), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").GetString().Should().Be("success");
        root.GetProperty("tag").GetInt32().Should().Be(41);
    }

    [Test]
    public async Task SessionGet_WithoutTag_PreservesNullTag()
    {
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "session-get" }), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").GetString().Should().Be("success");
        if (root.TryGetProperty("tag", out var tagProp))
        {
            tagProp.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task TorrentGet_WithCategory_ReportsDownloadDirAsCompletedDownloadFolder()
    {
        const string hash = "4123456789abcdef0123456789abcdef0123456c";
        const string name = "TransmissionSavePathVerificationMovie";
        const string category = "radarr";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn={name}";

        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        var addReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(
                JsonSerializer.Serialize(new
                {
                    method = "torrent-add",
                    arguments = new
                    {
                        filename = magnet,
                        paused = true,
                        labels = new[] { category },
                    },
                    tag = 50,
                }),
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        addReq.Headers.Add("X-Transmission-Session-Id", sessionId);

        var addResp = await this.Client.SendAsync(addReq);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            var getReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
            {
                Content = new System.Net.Http.StringContent(
                    JsonSerializer.Serialize(new
                    {
                        method = "torrent-get",
                        arguments = new
                        {
                            fields = new[] { "id", "name", "downloadDir", "labels" },
                            ids = new[] { hash },
                        },
                        tag = 51,
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            getReq.Headers.Add("X-Transmission-Session-Id", sessionId);

            var getResp = await this.Client.SendAsync(getReq);
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getJson = await getResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(getJson);
            var torrents = doc.RootElement.GetProperty("arguments").GetProperty("torrents");

            var matching = torrents.EnumerateArray().FirstOrDefault();
            matching.GetProperty("name").GetString().Should().Be(name);

            var downloadDir = matching.GetProperty("downloadDir").GetString();
            downloadDir.Should().NotBeNullOrWhiteSpace();
            downloadDir.Should().NotContain($"/{category}");
            downloadDir.Should().NotEndWith(category);
            downloadDir.Should().NotEndWith(name);
        }
        finally
        {
            var removeReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
            {
                Content = new System.Net.Http.StringContent(
                    JsonSerializer.Serialize(new
                    {
                        method = "torrent-remove",
                        arguments = new
                        {
                            ids = new[] { hash },
                            delete_local_data = true,
                        },
                        tag = 52,
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            removeReq.Headers.Add("X-Transmission-Session-Id", sessionId);
            await this.Client.SendAsync(removeReq);
        }
    }

    [Test]
    public async Task TorrentSet_AllPropertyMutations_Succeeds()
    {
        // 1. Get session id
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        // 2. Add a torrent for testing mutations
        const string setHash = "f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1";
        const string setName = "TransmissionSetTestTorrent";
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{setHash}&dn={setName}",
            category = "set-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // Helper to send transmission RPC request
            async Task SendTransmissionRpcAsync(object body)
            {
                var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
                {
                    Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Transmission-Session-Id", sessionId);
                var resp = await this.Client.SendAsync(req);
                resp.StatusCode.Should().Be(HttpStatusCode.OK);
                var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                doc.RootElement.GetProperty("result").GetString().Should().Be("success");
            }

            // 3. Mutate bandwidthPriority, labels, and limits
            await SendTransmissionRpcAsync(new
            {
                method = "torrent-set",
                arguments = new Dictionary<string, object>
                {
                    ["ids"] = new[] { torrentId },
                    ["bandwidthPriority"] = 1,
                    ["labels"] = new[] { "label-alpha", "label-beta" },
                    ["downloadLimit"] = 10240,
                    ["downloadLimited"] = true,
                    ["uploadLimit"] = 5120,
                    ["uploadLimited"] = true,
                    ["seedRatioMode"] = 1,
                    ["seedRatioLimit"] = 2.5,
                    ["seedIdleMode"] = 1,
                    ["seedIdleLimit"] = 90,
                },
                tag = 100,
            });

            // 4. Reset limits with downloadLimited = false and uploadLimited = false
            await SendTransmissionRpcAsync(new
            {
                method = "torrent-set",
                arguments = new Dictionary<string, object>
                {
                    ["ids"] = new[] { torrentId },
                    ["downloadLimited"] = false,
                    ["uploadLimited"] = false,
                    ["seedRatioMode"] = 0,
                    ["seedIdleMode"] = 0,
                },
                tag = 101,
            });

            // 5. Unlimited seedRatioMode = 2 and seedIdleMode = 2
            await SendTransmissionRpcAsync(new
            {
                method = "torrent-set",
                arguments = new Dictionary<string, object>
                {
                    ["ids"] = new[] { torrentId },
                    ["seedRatioMode"] = 2,
                    ["seedIdleMode"] = 2,
                },
                tag = 102,
            });

            // 6. Mutate trackers via trackerAdd, trackerList, and trackerReplace
            await SendTransmissionRpcAsync(new
            {
                method = "torrent-set",
                arguments = new Dictionary<string, object>
                {
                    ["ids"] = new[] { torrentId },
                    ["trackerAdd"] = new[] { "http://tracker.trans-add1.org:80/announce", "http://tracker.trans-add2.org:80/announce" },
                },
                tag = 103,
            });

            await SendTransmissionRpcAsync(new
            {
                method = "torrent-set",
                arguments = new Dictionary<string, object>
                {
                    ["ids"] = new[] { torrentId },
                    ["trackerList"] = "http://tracker.tlist1.org:80/announce\nhttp://tracker.tlist2.org:80/announce",
                },
                tag = 104,
            });

            // 7. Clear labels
            await SendTransmissionRpcAsync(new
            {
                method = "torrent-set",
                arguments = new Dictionary<string, object>
                {
                    ["ids"] = new[] { torrentId },
                    ["labels"] = System.Array.Empty<string>(),
                },
                tag = 105,
            });
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteData=true");
        }
    }

    [Test]
    public async Task SessionSet_AllConfigurationProperties_Succeeds()
    {
        // 1. Get session id
        var initial = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        // Helper
        async Task SendTransmissionRpcAsync(object body)
        {
            var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
            {
                Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Transmission-Session-Id", sessionId);
            var resp = await this.Client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("result").GetString().Should().Be("success");
        }

        // 2. Set full suite of session settings
        await SendTransmissionRpcAsync(new
        {
            method = "session-set",
            arguments = new Dictionary<string, object>
            {
                ["alt-speed-down"] = 512,
                ["alt-speed-up"] = 256,
                ["alt-speed-enabled"] = true,
                ["peer-port"] = 51413,
                ["speed-limit-down"] = 10240,
                ["speed-limit-down-enabled"] = true,
                ["speed-limit-up"] = 5120,
                ["speed-limit-up-enabled"] = true,
                ["seedRatioLimit"] = 2.0,
                ["seedRatioLimited"] = true,
                ["blocklist-enabled"] = false,
                ["blocklist-url"] = "http://example.com/blocklist.gz",
                ["script-torrent-done-filename"] = "/scripts/done.sh",
                ["script-torrent-added-filename"] = "/scripts/added.sh",
                ["script-torrent-done-seeding-filename"] = "/scripts/seeding.sh",
            },
            tag = 200,
        });

        // 3. Disable limits and alt-speed
        await SendTransmissionRpcAsync(new
        {
            method = "session-set",
            arguments = new Dictionary<string, object>
            {
                ["alt-speed-enabled"] = false,
                ["speed-limit-down-enabled"] = false,
                ["speed-limit-up-enabled"] = false,
                ["seedRatioLimited"] = false,
            },
            tag = 201,
        });
    }
}
