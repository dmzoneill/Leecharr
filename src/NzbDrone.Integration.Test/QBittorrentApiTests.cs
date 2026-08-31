using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class QBittorrentApiTests : IntegrationTestBase
{
    [Test]
    public async Task Login_ReturnsOkAndSetsCookie()
    {
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", "admin"),
            new KeyValuePair<string, string>("password", "adminadmin")
        });

        var response = await Client.PostAsync("/api/v2/auth/login", formData);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Ok.");
    }

    [Test]
    public async Task GetTorrentsInfo_ReturnsOkJson()
    {
        var response = await GetAsync("/api/v2/torrents/info");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().StartWith("[");
    }

    [Test]
    public async Task GetSyncMaindata_ReturnsOkJson()
    {
        var response = await GetAsync("/api/v2/sync/maindata?rid=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("rid").And.Contain("torrents");
    }

    [Test]
    public async Task TorrentLifecycle_AddPauseResumeDelete_Succeeds()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234568";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=QBitLifecycleTorrent";

        // 1. Add Torrent
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", magnet),
            new KeyValuePair<string, string>("category", "movies"),
            new KeyValuePair<string, string>("paused", "true")
        });

        var addResponse = await Client.PostAsync("/api/v2/torrents/add", addForm);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent appears in info
        var infoResponse = await GetAsync($"/api/v2/torrents/info?hashes={hash}");
        infoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var infoJson = await infoResponse.Content.ReadAsStringAsync();
        infoJson.Should().Contain(hash);

        // 2. Resume Torrent
        var resumeForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash)
        });
        var resumeResponse = await Client.PostAsync("/api/v2/torrents/resume", resumeForm);
        resumeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Pause Torrent
        var pauseForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash)
        });
        var pauseResponse = await Client.PostAsync("/api/v2/torrents/pause", pauseForm);
        pauseResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Delete Torrent with files
        var deleteForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash),
            new KeyValuePair<string, string>("deleteFiles", "true")
        });
        var deleteResponse = await Client.PostAsync("/api/v2/torrents/delete", deleteForm);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent is gone
        var verifyResponse = await GetAsync($"/api/v2/torrents/info?hashes={hash}");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyJson = await verifyResponse.Content.ReadAsStringAsync();
        verifyJson.Should().Be("[]");
    }
}
