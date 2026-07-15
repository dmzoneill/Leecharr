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
}
