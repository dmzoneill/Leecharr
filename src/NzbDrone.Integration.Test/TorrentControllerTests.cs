// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Torrents;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentControllerTests : IntegrationTestBase
{
    [Test]
    public async Task Torrents_GetAll_ReturnsOkList()
    {
        var response = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
        response.Should().NotBeNull();
    }

    [Test]
    public async Task Torrents_AddMagnetAndPauseResume_Succeeds()
    {
        // 1. Add torrent magnet via multipart form
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=TestTorrent"), "magnetUrl");
        form.Add(new StringContent("tv"), "category");
        form.Add(new StringContent("true"), "paused");

        var postResponse = await this.Client.PostAsync("/api/v1/torrents", form);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = Deserialize<TorrentResource>(await postResponse.Content.ReadAsStringAsync());
        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("TestTorrent");

        // 2. Pause & Resume
        var pauseResp = await this.PostJsonAsync($"/api/v1/torrents/{created.Id}/pause", new { });
        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resumeResp = await this.PostJsonAsync($"/api/v1/torrents/{created.Id}/resume", new { });
        resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Delete
        var deleteResp = await this.DeleteAsync($"/api/v1/torrents/{created.Id}?deleteFiles=false");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
