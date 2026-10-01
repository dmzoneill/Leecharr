// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentControllerNegativeBranchIntegrationTests : IntegrationTestBase
{
    private int existingTorrentId;

    [SetUp]
    public async Task SetupExistingTorrent()
    {
        var addPayload = new
        {
            magnetLink = "magnet:?xt=urn:btih:b123456789abcdef0123456789abcdef01234567&dn=NegativeBranchFixture&tr=http://tracker.example.com/announce",
            category = "test-cat",
            paused = true,
        };

        var resp = await this.PostJsonAsync("/api/v1/torrents", addPayload);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        this.existingTorrentId = doc.RootElement.GetProperty("id").GetInt32();
        this.existingTorrentId.Should().BeGreaterThan(0);
    }

    [TearDown]
    public async Task CleanupExistingTorrent()
    {
        if (this.existingTorrentId > 0)
        {
            await this.DeleteAsync($"/api/v1/torrents/{this.existingTorrentId}?deleteFiles=false");
        }
    }

    // -------------------------------------------------------------
    // Section 1: Invalid IDs and missing entities
    // -------------------------------------------------------------

    [Test]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrents/-1");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrents/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetFiles_NonExistentId_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrents/999999/files");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetTrackers_NonExistentId_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrents/999999/trackers");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetPeers_NonExistentId_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrents/999999/peers");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Update_NegativeId_ReturnsNotFound()
    {
        var response = await this.PutJsonAsync("/api/v1/torrents/-1", new { name = "NegativeUpdate" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Update_NonExistentId_ReturnsNotFound()
    {
        var response = await this.PutJsonAsync("/api/v1/torrents/999999", new { name = "MissingUpdate" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Announce_NonExistentId_ExecutesWithoutThrow()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/999999/announce", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Queue_NonExistentId_ExecutesSuccessfully()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/999999/queue", new { position = "up" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task BanPeer_NonExistentId_EmptyIp_ReturnsBadRequest()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/999999/peers/ban", new { ip = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BanPeer_NegativeId_ValidIp_ReturnsNotFound()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/-1/peers/ban", new { ip = "192.168.1.100" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Delete_NegativeId_ReturnsNoContent()
    {
        var response = await this.DeleteAsync("/api/v1/torrents/-1");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // -------------------------------------------------------------
    // Section 2: Input validation and boundary checking
    // -------------------------------------------------------------

    [Test]
    public async Task AddTorrentJson_NullJson_ReturnsBadRequest()
    {
        var content = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await this.Client.PostAsync("/api/v1/torrents", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddTorrentJson_EmptyJson_ReturnsBadRequest()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await this.Client.PostAsync("/api/v1/torrents", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddTorrentJson_EmptyBodyString_ReturnsBadRequest()
    {
        var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        var response = await this.Client.PostAsync("/api/v1/torrents", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddTorrentJson_MissingMagnetAndDownloadUrl_ReturnsBadRequest()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents", new { category = "movies", paused = true });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SetFilePriority_NegativePriority_ReturnsBadRequest()
    {
        var response = await this.PutJsonAsync(
            $"/api/v1/torrents/{this.existingTorrentId}/files/9999/priority",
            new { priority = -1 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SetFilePriority_PriorityGreaterThan7_ReturnsBadRequest()
    {
        var response = await this.PutJsonAsync(
            $"/api/v1/torrents/{this.existingTorrentId}/files/9999/priority",
            new { priority = 8 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SetFilePriorities_EmptyFilesList_ReturnsBadRequest()
    {
        var response = await this.PutJsonAsync(
            $"/api/v1/torrents/{this.existingTorrentId}/files/priorities",
            new { files = Array.Empty<object>() });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BulkAction_EmptyAction_ReturnsBadRequest()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/bulk", new
        {
            torrentIds = new[] { this.existingTorrentId },
            action = string.Empty,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BulkAction_EmptyTorrentIds_ReturnsOk()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/bulk", new
        {
            torrentIds = Array.Empty<int>(),
            action = "pause",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetPlaylistM3u_NonExistentTorrent_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrent/999999/playlist.m3u");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetSubtitles_NonExistentTorrent_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrent/999999/subtitles");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------
    // Section 3: Stream file and download file boundaries
    // -------------------------------------------------------------

    [Test]
    public async Task StreamFile_NonExistentTorrentAndFile_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrent/999999/files/9999/stream");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DownloadFile_NonExistentTorrentAndFile_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrent/999999/files/9999/download");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------
    // Section 4: Additional edge cases and client controller branches
    // -------------------------------------------------------------

    [Test]
    public async Task GetLogs_NonExistentId_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrents/999999/logs");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Pause_NonExistentId_ReturnsNotFound()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/999999/pause", new { });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Resume_NonExistentId_ReturnsNotFound()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/999999/resume", new { });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Recheck_NonExistentId_ReturnsNotFound()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/999999/recheck", new { });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AddTracker_NonExistentTorrent_ReturnsNotFound()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/999999/trackers", new
        {
            url = "http://tracker.example.com:8080/announce",
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AddTracker_EmptyUrl_ReturnsBadRequest()
    {
        var response = await this.PostJsonAsync($"/api/v1/torrents/{this.existingTorrentId}/trackers", new
        {
            url = string.Empty,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SetFilePriority_NonExistentFileOnExistingTorrent_ReturnsNotFound()
    {
        var response = await this.PutJsonAsync(
            $"/api/v1/torrents/{this.existingTorrentId}/files/9999/priority",
            new { priority = 3 });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SetFilePriorities_InvalidItemPriority_ReturnsBadRequest()
    {
        var response = await this.PutJsonAsync(
            $"/api/v1/torrents/{this.existingTorrentId}/files/priorities",
            new
            {
                files = new[]
                {
                    new { fileId = 1, priority = 99 },
                },
            });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateTorrent_MissingPath_ReturnsBadRequest()
    {
        var response = await this.PostJsonAsync("/api/v1/torrents/create", new
        {
            path = string.Empty,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Queue_NullBody_ReturnsBadRequest()
    {
        var content = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await this.Client.PostAsync($"/api/v1/torrents/{this.existingTorrentId}/queue", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StreamPlaylistM3u_NonExistentTorrent_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrent/999999/files/9999/playlist.m3u");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task FileSubtitles_NonExistentTorrent_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v1/torrent/999999/files/9999/subtitles");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task QBittorrent_GetProperties_NonExistentHash_ReturnsNotFound()
    {
        var response = await this.GetAsync("/api/v2/torrents/properties?hash=0000000000000000000000000000000000000000");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DelugeRpc_UnknownMethod_ReturnsErrorResponse()
    {
        var rpcBody = new
        {
            method = "daemon.non_existent_method_xyz",
            @params = Array.Empty<object>(),
            id = 1234,
        };

        var response = await this.PostJsonAsync("/json", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out var errorElem).Should().BeTrue();
        errorElem.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TransmissionRpc_WithoutSessionHeader_ReturnsConflict409()
    {
        var rpcBody = new
        {
            method = "session-get",
        };

        var response = await this.PostJsonAsync("/transmission/rpc", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Headers.Contains("X-Transmission-Session-Id").Should().BeTrue();
    }

    [Test]
    public async Task TransmissionRpc_UnknownMethod_ReturnsError()
    {
        var initialResp = await this.PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        initialResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var sessionId = System.Linq.Enumerable.First(initialResp.Headers.GetValues("X-Transmission-Session-Id"));

        var rpcBody = new
        {
            method = "unknown-method-test",
            arguments = new { },
            tag = 99,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
        {
            Content = new StringContent(JsonSerializer.Serialize(rpcBody), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var resultProp = doc.RootElement.GetProperty("result").GetString();
        resultProp.Should().NotBe("success");
    }
}
