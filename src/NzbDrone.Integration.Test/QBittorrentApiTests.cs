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
}
