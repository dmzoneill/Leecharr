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

    [Test]
    public async Task Torrents_Update_PersistsSeedingGoalsForceStartAndLabels()
    {
        // 1. Add torrent
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234568&dn=UpdateTestTorrent"), "magnetUrl");
        form.Add(new StringContent("tv"), "category");
        form.Add(new StringContent("true"), "paused");

        var postResponse = await this.Client.PostAsync("/api/v1/torrents", form);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = Deserialize<TorrentResource>(await postResponse.Content.ReadAsStringAsync());

        // 2. PUT /api/v1/torrent/{id} with updated fields
        var updatePayload = new TorrentResource
        {
            Id = created.Id,
            Name = created.Name,
            ForceStart = true,
            TargetRatio = 3.5,
            TargetSeedTimeMinutes = 180,
            ShareLimitAction = "Remove",
            Category = "movies",
            Label = "4k",
        };

        var putResponse = await this.PutJsonAsync($"/api/v1/torrent/{created.Id}", updatePayload);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = Deserialize<TorrentResource>(await putResponse.Content.ReadAsStringAsync());

        updated.ForceStart.Should().Be(true);
        updated.TargetRatio.Should().Be(3.5);
        updated.TargetSeedTimeMinutes.Should().Be(180);
        updated.ShareLimitAction.Should().Be("Remove");
        updated.Category.Should().Be("movies");
        updated.Label.Should().Be("4k");

        // Verify with GET
        var getResponse = await this.GetJsonAsync<TorrentResource>($"/api/v1/torrent/{created.Id}");
        getResponse.ForceStart.Should().Be(true);
        getResponse.TargetRatio.Should().Be(3.5);
        getResponse.TargetSeedTimeMinutes.Should().Be(180);
        getResponse.ShareLimitAction.Should().Be("Remove");
        getResponse.Category.Should().Be("movies");
        getResponse.Label.Should().Be("4k");

        // 3. Clean up
        await this.DeleteAsync($"/api/v1/torrents/{created.Id}?deleteFiles=false");
    }
}
