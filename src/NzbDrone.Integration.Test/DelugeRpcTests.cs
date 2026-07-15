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
}
