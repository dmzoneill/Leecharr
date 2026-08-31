using System.Collections.Generic;
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
            tag = 1
        };

        var response = await PostJsonAsync("/transmission/rpc", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Headers.Should().ContainKey("X-Transmission-Session-Id");
    }

    [Test]
    public async Task SessionGet_WithSessionHeader_ReturnsSuccess()
    {
        // 1. Initial request to get session id
        var initial = await PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        initial.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        // 2. Subsequent request with session header
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "session-get", tag = 10 }), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await Client.SendAsync(request);
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
        var initial = await PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
        {
            Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { method = "torrent-get", tag = 11 }), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await Client.SendAsync(request);
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
        var initial = await PostJsonAsync("/transmission/rpc", new { method = "session-get" });
        var sessionId = initial.Headers.GetValues("X-Transmission-Session-Id");

        async Task<JsonElement> SendRpcAsync(object body)
        {
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/transmission/rpc")
            {
                Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Transmission-Session-Id", sessionId);

            var resp = await Client.SendAsync(request);
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
                { "paused", true }
            },
            tag = 20
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
                { "ids", new[] { torrentId } }
            },
            tag = 21
        });
        startResult.GetProperty("result").GetString().Should().Be("success");

        // 4. Stop Torrent (Pause)
        var stopResult = await SendRpcAsync(new
        {
            method = "torrent-stop",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } }
            },
            tag = 22
        });
        stopResult.GetProperty("result").GetString().Should().Be("success");

        // 5. Remove Torrent (with local data)
        var removeResult = await SendRpcAsync(new
        {
            method = "torrent-remove",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } },
                { "delete-local-data", true }
            },
            tag = 23
        });
        removeResult.GetProperty("result").GetString().Should().Be("success");

        // 6. Verify Torrent is gone
        var getResult = await SendRpcAsync(new
        {
            method = "torrent-get",
            arguments = new Dictionary<string, object>
            {
                { "ids", new[] { torrentId } }
            },
            tag = 24
        });
        getResult.GetProperty("result").GetString().Should().Be("success");
        getResult.GetProperty("arguments").GetProperty("torrents").GetArrayLength().Should().Be(0);
    }
}
