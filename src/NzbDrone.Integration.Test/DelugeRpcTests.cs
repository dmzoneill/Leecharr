// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DelugeRpcTests : IntegrationTestBase
{
    [Test]
    public async Task AuthLogin_ReturnsSuccessResult()
    {
        var rpcBody = new
        {
            method = "auth.login",
            @params = new object[] { "deluge" },
            id = 1,
        };

        var response = await this.PostJsonAsync("/json", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").GetBoolean().Should().BeTrue();
        if (root.TryGetProperty("error", out var err))
        {
            err.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task CoreGetTorrentsStatus_ReturnsTorrentsDictionary()
    {
        var rpcBody = new
        {
            method = "core.get_torrents_status",
            @params = new object[] { new { }, new string[] { "name", "state", "progress" } },
            id = 2,
        };

        var response = await this.PostJsonAsync("/json", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Object);
        if (root.TryGetProperty("error", out var err))
        {
            err.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task CoreGetFilterTree_ReturnsStateAndLabelTrees()
    {
        var rpcBody = new
        {
            method = "core.get_filter_tree",
            @params = new object[] { },
            id = 3,
        };

        var response = await this.PostJsonAsync("/json", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = root.GetProperty("result");
        result.TryGetProperty("state", out _).Should().BeTrue();
        result.TryGetProperty("label", out _).Should().BeTrue();
    }

    [Test]
    public async Task TorrentLifecycle_AddPauseResumeDelete_Succeeds()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234569";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=DelugeLifecycleTorrent";

        // 1. Add Torrent Magnet
        var addRpc = new
        {
            method = "core.add_torrent_magnet",
            @params = new object[] { magnet, new { add_paused = true, label = "tv" } },
            id = 10,
        };
        var addResp = await this.PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var addJson = await addResp.Content.ReadAsStringAsync();
        using var addDoc = JsonDocument.Parse(addJson);
        addDoc.RootElement.GetProperty("result").GetString().Should().Be(hash);

        // Verify status
        var statusRpc = new
        {
            method = "core.get_torrent_status",
            @params = new object[] { hash, new[] { "name", "state" } },
            id = 11,
        };
        var statusResp = await this.PostJsonAsync("/json", statusRpc);
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = await statusResp.Content.ReadAsStringAsync();
        using var statusDoc = JsonDocument.Parse(statusJson);
        statusDoc.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("DelugeLifecycleTorrent");

        // 2. Resume Torrent
        var resumeRpc = new
        {
            method = "core.resume_torrent",
            @params = new object[] { hash },
            id = 12,
        };
        var resumeResp = await this.PostJsonAsync("/json", resumeRpc);
        resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Pause Torrent
        var pauseRpc = new
        {
            method = "core.pause_torrent",
            @params = new object[] { hash },
            id = 13,
        };
        var pauseResp = await this.PostJsonAsync("/json", pauseRpc);
        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Remove Torrent with files
        var removeRpc = new
        {
            method = "core.remove_torrent",
            @params = new object[] { hash, true },
            id = 14,
        };
        var removeResp = await this.PostJsonAsync("/json", removeRpc);
        removeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent is gone
        var verifyResp = await this.PostJsonAsync("/json", statusRpc);
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyJson = await verifyResp.Content.ReadAsStringAsync();
        using var verifyDoc = JsonDocument.Parse(verifyJson);
        verifyDoc.RootElement.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task CoreGetTorrentsStatus_WithStandardMetrics_ReturnsRequestedFields()
    {
        const string hash = "fedcba9876543210fedcba9876543210fedcba98";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=DelugeMetricsTorrent&tr=http%3A%2F%2Ftracker.test.org%2Fannounce";

        var addRpc = new
        {
            method = "core.add_torrent_magnet",
            @params = new object[] { magnet, new { add_paused = true, label = "tv" } },
            id = 20,
        };
        var addResp = await this.PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusRpc = new
        {
            method = "core.get_torrents_status",
            @params = new object[]
            {
                new { },
                new[]
                {
                    "name",
                    "download_location",
                    "queue",
                    "queue_position",
                    "num_pieces",
                    "piece_length",
                    "tracker_host",
                    "trackers",
                    "total_wanted",
                    "comment",
                },
            },
            id = 21,
        };

        var statusResp = await this.PostJsonAsync("/json", statusRpc);
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusJson = await statusResp.Content.ReadAsStringAsync();
        using var statusDoc = JsonDocument.Parse(statusJson);
        var root = statusDoc.RootElement;
        var result = root.GetProperty("result");

        result.TryGetProperty(hash.ToLowerInvariant(), out var torrentElem).Should().BeTrue();
        torrentElem.TryGetProperty("download_location", out _).Should().BeTrue();
        torrentElem.TryGetProperty("queue", out _).Should().BeTrue();
        torrentElem.TryGetProperty("num_pieces", out _).Should().BeTrue();
        torrentElem.TryGetProperty("piece_length", out _).Should().BeTrue();
        torrentElem.TryGetProperty("tracker_host", out _).Should().BeTrue();
        torrentElem.TryGetProperty("trackers", out _).Should().BeTrue();
        torrentElem.TryGetProperty("total_wanted", out _).Should().BeTrue();
        torrentElem.TryGetProperty("comment", out _).Should().BeTrue();

        // Cleanup
        var removeRpc = new
        {
            method = "core.remove_torrent",
            @params = new object[] { hash, true },
            id = 22,
        };
        await this.PostJsonAsync("/json", removeRpc);
    }

    [Test]
    public async Task AddTorrent_WithCategory_ReportsSavePathAsCompletedDownloadFolder()
    {
        const string hash = "112233445566778899aabbccddeeff0011223344";
        const string name = "SavePathVerificationMovie";
        const string category = "radarr";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn={name}";

        // 1. Add label 'radarr'
        var labelRpc = new
        {
            method = "label.add",
            @params = new object[] { category },
            id = 30,
        };
        var labelResp = await this.PostJsonAsync("/json", labelRpc);
        labelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 2. Add Torrent Magnet with label 'radarr'
            var addRpc = new
            {
                method = "core.add_torrent_magnet",
                @params = new object[] { magnet, new { add_paused = true, label = category } },
                id = 31,
            };
            var addResp = await this.PostJsonAsync("/json", addRpc);
            addResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Verify save_path via core.get_torrent_status
            var statusRpc = new
            {
                method = "core.get_torrent_status",
                @params = new object[] { hash, new[] { "name", "save_path", "download_location", "label" } },
                id = 32,
            };
            var statusResp = await this.PostJsonAsync("/json", statusRpc);
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusJson = await statusResp.Content.ReadAsStringAsync();
            using var statusDoc = JsonDocument.Parse(statusJson);
            var result = statusDoc.RootElement.GetProperty("result");

            result.GetProperty("name").GetString().Should().Be(name);
            result.GetProperty("label").GetString().Should().Be(category);

            var savePath = result.GetProperty("save_path").GetString();
            var downloadLocation = result.GetProperty("download_location").GetString();

            savePath.Should().NotBeNullOrWhiteSpace();
            savePath.Should().NotContain($"/{category}");
            savePath.Should().NotEndWith(category);
            savePath.Should().NotEndWith(name);

            downloadLocation.Should().Be(savePath);

            // 4. Verify save_path via web.update_ui (used by Radarr/Sonarr)
            var updateUiRpc = new
            {
                method = "web.update_ui",
                @params = new object[] { new[] { "name", "save_path" }, new { } },
                id = 33,
            };
            var updateUiResp = await this.PostJsonAsync("/json", updateUiRpc);
            updateUiResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateUiJson = await updateUiResp.Content.ReadAsStringAsync();
            using var updateUiDoc = JsonDocument.Parse(updateUiJson);
            var torrents = updateUiDoc.RootElement.GetProperty("result").GetProperty("torrents");

            torrents.TryGetProperty(hash.ToLowerInvariant(), out var uiTorrent).Should().BeTrue();
            var uiSavePath = uiTorrent.GetProperty("save_path").GetString();
            uiSavePath.Should().Be(savePath);
        }
        finally
        {
            var removeRpc = new
            {
                method = "core.remove_torrent",
                @params = new object[] { hash, true },
                id = 34,
            };
            await this.PostJsonAsync("/json", removeRpc);
        }
    }
}
