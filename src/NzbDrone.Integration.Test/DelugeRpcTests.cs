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
            id = 1
        };

        var response = await PostJsonAsync("/json", rpcBody);
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
            id = 2
        };

        var response = await PostJsonAsync("/json", rpcBody);
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
            id = 3
        };

        var response = await PostJsonAsync("/json", rpcBody);
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
            id = 10
        };
        var addResp = await PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var addJson = await addResp.Content.ReadAsStringAsync();
        using var addDoc = JsonDocument.Parse(addJson);
        addDoc.RootElement.GetProperty("result").GetString().Should().Be(hash);

        // Verify status
        var statusRpc = new
        {
            method = "core.get_torrent_status",
            @params = new object[] { hash, new[] { "name", "state" } },
            id = 11
        };
        var statusResp = await PostJsonAsync("/json", statusRpc);
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = await statusResp.Content.ReadAsStringAsync();
        using var statusDoc = JsonDocument.Parse(statusJson);
        statusDoc.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("DelugeLifecycleTorrent");

        // 2. Resume Torrent
        var resumeRpc = new
        {
            method = "core.resume_torrent",
            @params = new object[] { hash },
            id = 12
        };
        var resumeResp = await PostJsonAsync("/json", resumeRpc);
        resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Pause Torrent
        var pauseRpc = new
        {
            method = "core.pause_torrent",
            @params = new object[] { hash },
            id = 13
        };
        var pauseResp = await PostJsonAsync("/json", pauseRpc);
        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Remove Torrent with files
        var removeRpc = new
        {
            method = "core.remove_torrent",
            @params = new object[] { hash, true },
            id = 14
        };
        var removeResp = await PostJsonAsync("/json", removeRpc);
        removeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent is gone
        var verifyResp = await PostJsonAsync("/json", statusRpc);
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyJson = await verifyResp.Content.ReadAsStringAsync();
        using var verifyDoc = JsonDocument.Parse(verifyJson);
        verifyDoc.RootElement.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
