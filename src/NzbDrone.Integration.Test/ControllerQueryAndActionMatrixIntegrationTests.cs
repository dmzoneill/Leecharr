// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ControllerQueryAndActionMatrixIntegrationTests : IntegrationTestBase
{
    private const string Hash1 = "a000000000000000000000000000000000000001";
    private const string Name1 = "MatrixTorrentAlpha";
    private const string Hash2 = "a000000000000000000000000000000000000002";
    private const string Name2 = "MatrixTorrentBeta";

    private string transmissionSessionId;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // 1. Authenticate with qBittorrent emulation
        var loginForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", "admin"),
            new KeyValuePair<string, string>("password", "adminadmin"),
        });
        var loginResp = await this.Client.PostAsync("/api/v2/auth/login", loginForm);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Add test torrents
        var addForm1 = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{Hash1}&dn={Name1}"),
            new KeyValuePair<string, string>("category", "movies"),
            new KeyValuePair<string, string>("paused", "true"),
        });
        var addResp1 = await this.Client.PostAsync("/api/v2/torrents/add", addForm1);
        addResp1.StatusCode.Should().Be(HttpStatusCode.OK);

        var addForm2 = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{Hash2}&dn={Name2}"),
            new KeyValuePair<string, string>("category", "tv"),
            new KeyValuePair<string, string>("paused", "false"),
        });
        var addResp2 = await this.Client.PostAsync("/api/v2/torrents/add", addForm2);
        addResp2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        var deleteForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", $"{Hash1}|{Hash2}"),
            new KeyValuePair<string, string>("deleteFiles", "true"),
        });
        await this.Client.PostAsync("/api/v2/torrents/delete", deleteForm);
    }

    // ==========================================
    // 1. QBittorrentApiController Tests
    // ==========================================

    [Test]
    public async Task QBit_TorrentsInfo_AllSortCombinations_Succeeds(
        [Values("name", "size", "progress", "eta", "ratio", "priority", "added_on", "num_seeds", "downloaded")] string sortKey,
        [Values(false, true)] bool reverse)
    {
        var response = await this.GetAsync($"/api/v2/torrents/info?sort={sortKey}&reverse={reverse.ToString().ToLowerInvariant()}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task QBit_TorrentsInfo_AllFilterValues_Succeeds(
        [Values("all", "downloading", "seeding", "completed", "paused", "active", "inactive", "resumed", "stalled", "checking", "errored")] string filterValue)
    {
        var response = await this.GetAsync($"/api/v2/torrents/info?filter={filterValue}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task QBit_TorrentsInfo_LimitAndOffset_Succeeds()
    {
        var response = await this.GetAsync("/api/v2/torrents/info?limit=5&offset=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeInRange(1, 5);
    }

    [Test]
    public async Task QBit_TorrentsInfo_MultipleHashes_Succeeds()
    {
        var response = await this.GetAsync($"/api/v2/torrents/info?hashes={Hash1}|{Hash2}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var returnedHashes = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("hash").GetString()?.ToLowerInvariant())
            .ToList();

        returnedHashes.Should().Contain(Hash1.ToLowerInvariant());
        returnedHashes.Should().Contain(Hash2.ToLowerInvariant());
    }

    [Test]
    public async Task QBit_Torrents_PieceStatesAndPieceHashes_Succeeds()
    {
        // 1. PieceStates
        var statesResponse = await this.GetAsync($"/api/v2/torrents/pieceStates?hash={Hash1}");
        statesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var statesJson = await statesResponse.Content.ReadAsStringAsync();
        using var statesDoc = JsonDocument.Parse(statesJson);
        statesDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // 2. PieceHashes
        var hashesResponse = await this.GetAsync($"/api/v2/torrents/pieceHashes?hash={Hash1}");
        hashesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var hashesJson = await hashesResponse.Content.ReadAsStringAsync();
        using var hashesDoc = JsonDocument.Parse(hashesJson);
        hashesDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task QBit_Torrents_WebSeeds_Succeeds()
    {
        var response = await this.GetAsync($"/api/v2/torrents/webseeds?hash={Hash1}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task QBit_Torrents_Files_Succeeds()
    {
        var response = await this.GetAsync($"/api/v2/torrents/files?hash={Hash1}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task QBit_SearchEndpoints_FullLifecycle_Succeeds()
    {
        // 1. /api/v2/search/plugins
        var pluginsResp = await this.GetAsync("/api/v2/search/plugins");
        pluginsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pluginsJson = await pluginsResp.Content.ReadAsStringAsync();
        using var pluginsDoc = JsonDocument.Parse(pluginsJson);
        pluginsDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // 2. /api/v2/search/start
        var startResp = await this.Client.PostAsync(
            "/api/v2/search/start",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("pattern", "ubuntu"),
                new KeyValuePair<string, string>("plugins", "all"),
                new KeyValuePair<string, string>("category", "all"),
            }));
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var startJson = await startResp.Content.ReadAsStringAsync();
        using var startDoc = JsonDocument.Parse(startJson);
        startDoc.RootElement.TryGetProperty("id", out var idProp).Should().BeTrue();
        var searchId = idProp.GetInt32();
        searchId.Should().BeGreaterThan(0);

        // 3. /api/v2/search/status
        var statusResp = await this.GetAsync($"/api/v2/search/status?id={searchId}");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = await statusResp.Content.ReadAsStringAsync();
        using var statusDoc = JsonDocument.Parse(statusJson);
        statusDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        statusDoc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        // 4. /api/v2/search/results
        var resultsResp = await this.GetAsync($"/api/v2/search/results?id={searchId}&limit=10&offset=0");
        resultsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultsJson = await resultsResp.Content.ReadAsStringAsync();
        using var resultsDoc = JsonDocument.Parse(resultsJson);
        resultsDoc.RootElement.TryGetProperty("results", out _).Should().BeTrue();
        resultsDoc.RootElement.TryGetProperty("status", out _).Should().BeTrue();

        // 5. /api/v2/search/stop
        var stopResp = await this.Client.PostAsync(
            "/api/v2/search/stop",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", searchId.ToString()),
            }));
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. /api/v2/search/delete
        var deleteResp = await this.Client.PostAsync(
            "/api/v2/search/delete",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", searchId.ToString()),
            }));
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==========================================
    // 2. DelugeJsonRpcController Tests
    // ==========================================

    [Test]
    public async Task Deluge_CoreGetTorrentsStatus_RequestingAllFields_Succeeds()
    {
        var allRequestedFields = new[]
        {
            "name", "hash", "total_size", "progress", "eta", "state",
            "download_payload_rate", "upload_payload_rate", "ratio",
            "num_seeds", "total_seeds", "num_peers", "total_peers",
            "save_path", "time_added", "active_time", "seeding_time",
            "seed_rank", "label", "is_finished", "tracker_host", "message", "files",
        };

        var doc = await this.SendDelugeRpcAsync("core.get_torrents_status", new object[]
        {
            new { },
            allRequestedFields,
        });

        var result = doc.RootElement.GetProperty("result");
        result.ValueKind.Should().Be(JsonValueKind.Object);

        // Find Hash1 in result
        result.TryGetProperty(Hash1.ToLowerInvariant(), out var torrentElem).Should().BeTrue();

        foreach (var field in allRequestedFields)
        {
            torrentElem.TryGetProperty(field, out _).Should().BeTrue($"field '{field}' must be returned by core.get_torrents_status");
        }
    }

    [Test]
    public async Task Deluge_CoreGetConfigAndSetConfig_Succeeds()
    {
        // 1. core.get_config
        var getDoc = await this.SendDelugeRpcAsync("core.get_config");
        var result = getDoc.RootElement.GetProperty("result");
        result.ValueKind.Should().Be(JsonValueKind.Object);
        result.TryGetProperty("download_location", out _).Should().BeTrue();

        // 2. core.set_config
        var setDoc = await this.SendDelugeRpcAsync("core.set_config", new object[]
        {
            new Dictionary<string, object>
            {
                { "max_connections_global", 350 },
            },
        });
        setDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task Deluge_CoreGetFreeSpace_Succeeds()
    {
        var doc = await this.SendDelugeRpcAsync("core.get_free_space", new object[] { "/downloads" });
        var result = doc.RootElement.GetProperty("result");
        result.GetInt64().Should().BeGreaterThanOrEqualTo(0L);
    }

    [Test]
    public async Task Deluge_CoreGetSessionStatus_Succeeds()
    {
        var doc = await this.SendDelugeRpcAsync("core.get_session_status");
        var result = doc.RootElement.GetProperty("result");
        result.ValueKind.Should().Be(JsonValueKind.Object);
        result.TryGetProperty("download_rate", out _).Should().BeTrue();
        result.TryGetProperty("upload_rate", out _).Should().BeTrue();
        result.TryGetProperty("total_download", out _).Should().BeTrue();
        result.TryGetProperty("total_upload", out _).Should().BeTrue();
    }

    [Test]
    public async Task Deluge_CoreGetPathSize_Succeeds()
    {
        // Existing path returns non-negative size
        var currentDir = Directory.GetCurrentDirectory();
        var validDoc = await this.SendDelugeRpcAsync("core.get_path_size", new object[] { currentDir });
        var validResult = validDoc.RootElement.GetProperty("result");
        validResult.GetInt64().Should().BeGreaterThanOrEqualTo(0L);

        // Non-existent path returns -1
        var invalidDoc = await this.SendDelugeRpcAsync("core.get_path_size", new object[] { "/invalid/path/that/does/not/exist/anywhere" });
        var invalidResult = invalidDoc.RootElement.GetProperty("result");
        invalidResult.GetInt64().Should().Be(-1L);
    }

    [Test]
    public async Task Deluge_CoreGetFilterTree_Succeeds()
    {
        var doc = await this.SendDelugeRpcAsync("core.get_filter_tree");
        var result = doc.RootElement.GetProperty("result");
        result.ValueKind.Should().Be(JsonValueKind.Object);
        result.TryGetProperty("state", out var stateElem).Should().BeTrue();
        result.TryGetProperty("label", out var labelElem).Should().BeTrue();
        stateElem.ValueKind.Should().Be(JsonValueKind.Array);
        labelElem.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ==========================================
    // 3. TransmissionRpcController Tests
    // ==========================================

    [Test]
    public async Task Transmission_SessionGetAndSet_Succeeds()
    {
        // 1. session-get
        var getDoc = await this.SendTransmissionRpcAsync(new { method = "session-get", tag = 101 });
        getDoc.RootElement.GetProperty("result").GetString().Should().Be("success");
        var args = getDoc.RootElement.GetProperty("arguments");
        args.TryGetProperty("version", out _).Should().BeTrue();
        args.TryGetProperty("rpc-version", out _).Should().BeTrue();

        // 2. session-set
        var setDoc = await this.SendTransmissionRpcAsync(new
        {
            method = "session-set",
            arguments = new Dictionary<string, object>
            {
                { "speed-limit-down-enabled", false },
                { "speed-limit-up-enabled", false },
            },
            tag = 102,
        });
        setDoc.RootElement.GetProperty("result").GetString().Should().Be("success");
    }

    [Test]
    public async Task Transmission_SessionStats_Succeeds()
    {
        var doc = await this.SendTransmissionRpcAsync(new { method = "session-stats", tag = 103 });
        doc.RootElement.GetProperty("result").GetString().Should().Be("success");
        var args = doc.RootElement.GetProperty("arguments");
        args.TryGetProperty("torrentCount", out _).Should().BeTrue();
        args.TryGetProperty("downloadSpeed", out _).Should().BeTrue();
        args.TryGetProperty("uploadSpeed", out _).Should().BeTrue();
    }

    [Test]
    public async Task Transmission_TorrentGet_RequestingAllFields_Succeeds()
    {
        var allRequestedFields = new[]
        {
            "id", "name", "totalSize", "status", "downloadDir", "percentDone",
            "rateDownload", "rateUpload", "eta", "peersConnected", "peersGettingFromUs",
            "peersSendingToUs", "uploadRatio", "files", "fileStats", "trackers",
            "trackerStats", "pieceCount", "pieceSize", "pieces", "dateCreated",
            "dateAdded", "startDate", "doneDate", "secondsDownloading", "secondsSeeding",
            "activityDate",
        };

        var doc = await this.SendTransmissionRpcAsync(new
        {
            method = "torrent-get",
            arguments = new Dictionary<string, object>
            {
                { "fields", allRequestedFields },
            },
            tag = 104,
        });

        doc.RootElement.GetProperty("result").GetString().Should().Be("success");
        var torrents = doc.RootElement.GetProperty("arguments").GetProperty("torrents");
        torrents.ValueKind.Should().Be(JsonValueKind.Array);
        torrents.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        var firstTorrent = torrents[0];
        foreach (var field in allRequestedFields)
        {
            firstTorrent.TryGetProperty(field, out _).Should().BeTrue($"field '{field}' must be returned by Transmission torrent-get");
        }
    }

    [Test]
    public async Task Transmission_TorrentSet_LimitsAndModes_Succeeds()
    {
        var torrentId = await this.GetFirstTorrentIdAsync();

        var doc = await this.SendTransmissionRpcAsync(new
        {
            method = "torrent-set",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } },
                { "uploadLimit", 250 },
                { "downloadLimit", 500 },
                { "seedRatioLimit", 2.5 },
                { "seedRatioMode", 1 },
            },
            tag = 105,
        });

        doc.RootElement.GetProperty("result").GetString().Should().Be("success");
    }

    [Test]
    public async Task Transmission_QueueMoveActions_Succeeds()
    {
        var torrentId = await this.GetFirstTorrentIdAsync();

        foreach (var action in new[] { "queue-move-top", "queue-move-up", "queue-move-down", "queue-move-bottom" })
        {
            var doc = await this.SendTransmissionRpcAsync(new
            {
                method = action,
                arguments = new Dictionary<string, object>
                {
                    { "ids", new[] { torrentId } },
                },
                tag = 106,
            });

            doc.RootElement.GetProperty("result").GetString().Should().Be("success");
        }
    }

    [Test]
    public async Task Transmission_PortTestAndFreeSpace_Succeeds()
    {
        // 1. port-test
        var portDoc = await this.SendTransmissionRpcAsync(new { method = "port-test", tag = 107 });
        portDoc.RootElement.GetProperty("result").GetString().Should().Be("success");
        var portArgs = portDoc.RootElement.GetProperty("arguments");
        portArgs.TryGetProperty("port-is-open", out _).Should().BeTrue();

        // 2. free-space
        var spaceDoc = await this.SendTransmissionRpcAsync(new
        {
            method = "free-space",
            arguments = new Dictionary<string, object>
            {
                { "path", "/downloads" },
            },
            tag = 108,
        });
        spaceDoc.RootElement.GetProperty("result").GetString().Should().Be("success");
        var spaceArgs = spaceDoc.RootElement.GetProperty("arguments");
        spaceArgs.TryGetProperty("size-bytes", out var sizeBytes).Should().BeTrue();
        sizeBytes.GetInt64().Should().BeGreaterThanOrEqualTo(0L);
    }

    // ==========================================
    // Helpers
    // ==========================================

    private async Task<JsonDocument> SendDelugeRpcAsync(string method, object[] parameters = null, int id = 1)
    {
        var body = new
        {
            method,
            @params = parameters ?? Array.Empty<object>(),
            id,
        };

        var response = await this.PostJsonAsync("/json", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private async Task<JsonDocument> SendTransmissionRpcAsync(object body)
    {
        if (string.IsNullOrEmpty(this.transmissionSessionId))
        {
            var initResp = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
            if (initResp.Headers.TryGetValues("X-Transmission-Session-Id", out var vals))
            {
                this.transmissionSessionId = vals.FirstOrDefault();
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(this.transmissionSessionId))
        {
            request.Headers.Add("X-Transmission-Session-Id", this.transmissionSessionId);
        }

        var response = await this.Client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            if (response.Headers.TryGetValues("X-Transmission-Session-Id", out var vals))
            {
                this.transmissionSessionId = vals.FirstOrDefault();
            }

            var retryReq = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            retryReq.Headers.Add("X-Transmission-Session-Id", this.transmissionSessionId);
            response = await this.Client.SendAsync(retryReq);
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private async Task<int> GetFirstTorrentIdAsync()
    {
        var doc = await this.SendTransmissionRpcAsync(new
        {
            method = "torrent-get",
            arguments = new Dictionary<string, object>
            {
                { "fields", new[] { "id", "hashString" } },
            },
        });

        var torrents = doc.RootElement.GetProperty("arguments").GetProperty("torrents");
        foreach (var t in torrents.EnumerateArray())
        {
            return t.GetProperty("id").GetInt32();
        }

        return 1;
    }
}
