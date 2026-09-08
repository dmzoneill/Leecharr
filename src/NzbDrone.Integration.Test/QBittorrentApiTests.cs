// Copyright (c) PlaceholderCompany. All rights reserved.

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
            new KeyValuePair<string, string>("password", "adminadmin"),
        });

        var response = await this.Client.PostAsync("/api/v2/auth/login", formData);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Ok.");
    }

    [Test]
    public async Task GetTorrentsInfo_ReturnsOkJson()
    {
        var response = await this.GetAsync("/api/v2/torrents/info");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().StartWith("[");
    }

    [Test]
    public async Task GetSyncMaindata_ReturnsOkJson()
    {
        var response = await this.GetAsync("/api/v2/sync/maindata?rid=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("rid").And.Contain("torrents");
    }

    [Test]
    public async Task GetSyncMaindata_SubsequentPoll_ReturnsIncrementalUpdate()
    {
        var initialResponse = await this.GetAsync("/api/v2/sync/maindata?rid=0");
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialJson = await initialResponse.Content.ReadAsStringAsync();
        initialJson.Should().Contain("\"full_update\":true");

        var subsequentResponse = await this.GetAsync("/api/v2/sync/maindata?rid=1");
        subsequentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var subsequentJson = await subsequentResponse.Content.ReadAsStringAsync();
        subsequentJson.Should().Contain("\"full_update\":false");
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
            new KeyValuePair<string, string>("paused", "true"),
        });

        var addResponse = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent appears in info
        var infoResponse = await this.GetAsync($"/api/v2/torrents/info?hashes={hash}");
        infoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var infoJson = await infoResponse.Content.ReadAsStringAsync();
        infoJson.Should().Contain(hash);

        // 2. Resume Torrent
        var resumeForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash),
        });
        var resumeResponse = await this.Client.PostAsync("/api/v2/torrents/resume", resumeForm);
        resumeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Pause Torrent
        var pauseForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash),
        });
        var pauseResponse = await this.Client.PostAsync("/api/v2/torrents/pause", pauseForm);
        pauseResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Delete Torrent with files
        var deleteForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash),
            new KeyValuePair<string, string>("deleteFiles", "true"),
        });
        var deleteResponse = await this.Client.PostAsync("/api/v2/torrents/delete", deleteForm);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent is gone
        var verifyResponse = await this.GetAsync($"/api/v2/torrents/info?hashes={hash}");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyJson = await verifyResponse.Content.ReadAsStringAsync();
        verifyJson.Should().Be("[]");
    }

    [Test]
    public async Task TorrentLifecycle_BatchOperationsWithHashesAll_Succeeds()
    {
        const string hash = "1123456789abcdef0123456789abcdef01234569";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=QBitBatchAllTorrent";

        // 1. Add Torrent
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", magnet),
            new KeyValuePair<string, string>("category", "tv"),
            new KeyValuePair<string, string>("paused", "true"),
        });

        var addResponse = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent info with hashes=all
        var infoResponse = await this.GetAsync("/api/v2/torrents/info?hashes=all");
        infoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var infoJson = await infoResponse.Content.ReadAsStringAsync();
        infoJson.Should().Contain(hash);

        // 2. Resume all torrents with hashes=all
        var resumeForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", "all"),
        });
        var resumeResponse = await this.Client.PostAsync("/api/v2/torrents/resume", resumeForm);
        resumeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Pause all torrents with hashes=all
        var pauseForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", "all"),
        });
        var pauseResponse = await this.Client.PostAsync("/api/v2/torrents/pause", pauseForm);
        pauseResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Delete all torrents with hashes=all
        var deleteForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", "all"),
            new KeyValuePair<string, string>("deleteFiles", "true"),
        });
        var deleteResponse = await this.Client.PostAsync("/api/v2/torrents/delete", deleteForm);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify torrent is gone
        var verifyResponse = await this.GetAsync($"/api/v2/torrents/info?hashes={hash}");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyJson = await verifyResponse.Content.ReadAsStringAsync();
        verifyJson.Should().Be("[]");
    }
<<<<<<< HEAD

    [Test]
    public async Task GetTorrentsInfo_WithTagAndFilter_ReturnsFilteredResults()
    {
        const string hash1 = "1111111111111111111111111111111111111111";
        var magnet1 = $"magnet:?xt=urn:btih:{hash1}&dn=TagTestTorrent";

        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", magnet1),
            new KeyValuePair<string, string>("tags", "testtag, anothertag"),
            new KeyValuePair<string, string>("paused", "true"),
        });
        var addResponse = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Query with matching tag
        var tagMatchResponse = await this.GetAsync("/api/v2/torrents/info?tag=testtag");
        tagMatchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tagMatchJson = await tagMatchResponse.Content.ReadAsStringAsync();
        tagMatchJson.Should().Contain(hash1);

        // Query with non-matching tag
        var tagMismatchResponse = await this.GetAsync("/api/v2/torrents/info?tag=nonexistent");
        tagMismatchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tagMismatchJson = await tagMismatchResponse.Content.ReadAsStringAsync();
        tagMismatchJson.Should().NotContain(hash1);

        // Query with paused filter
        var pausedFilterResponse = await this.GetAsync("/api/v2/torrents/info?filter=paused");
        pausedFilterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pausedFilterJson = await pausedFilterResponse.Content.ReadAsStringAsync();
        pausedFilterJson.Should().Contain(hash1);

        // Query with resumed filter
        var resumedFilterResponse = await this.GetAsync("/api/v2/torrents/info?filter=resumed");
        resumedFilterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var resumedFilterJson = await resumedFilterResponse.Content.ReadAsStringAsync();
        resumedFilterJson.Should().NotContain(hash1);

        // Clean up
        var deleteForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash1),
            new KeyValuePair<string, string>("deleteFiles", "true"),
        });
        await this.Client.PostAsync("/api/v2/torrents/delete", deleteForm);
    }

    [Test]
    public async Task TorrentLifecycle_AddWithDownloadPath_SetsSavePath()
    {
        const string hash = "2123456789abcdef0123456789abcdef0123456a";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=QBitDownloadPathTorrent";
        var customPath = "/downloads/custom_qbit_dir";

        // Add Torrent with downloadPath parameter
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", magnet),
            new KeyValuePair<string, string>("downloadPath", customPath),
            new KeyValuePair<string, string>("paused", "true"),
        });

        var addResponse = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify save_path matches customPath in torrent info
        var infoResponse = await this.GetAsync($"/api/v2/torrents/info?hashes={hash}");
        infoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var infoJson = await infoResponse.Content.ReadAsStringAsync();
        infoJson.Should().Contain(hash);
        infoJson.Should().Contain(customPath);

        // Clean up
        var deleteForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash),
            new KeyValuePair<string, string>("deleteFiles", "true"),
        });
        await this.Client.PostAsync("/api/v2/torrents/delete", deleteForm);
    }
}
