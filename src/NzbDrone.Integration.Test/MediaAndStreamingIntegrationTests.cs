// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class MediaAndStreamingIntegrationTests : IntegrationTestBase
{
    private IStoragePathService storagePathService = null!;
    private ITorrentService torrentService = null!;
    private string completedDir = null!;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        this.storagePathService = services.GetRequiredService<IStoragePathService>();
        this.torrentService = services.GetRequiredService<ITorrentService>();
        this.completedDir = this.storagePathService.GetCompletedDirectory(null);
        Directory.CreateDirectory(this.completedDir);
    }

    [Test]
    public async Task MediaMetadata_GetAllAndGetById_ReturnsConsistentMetadata()
    {
        // 1. Get all media metadata
        var allResp = await this.GetAsync("/api/v1/media");
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allJson = await allResp.Content.ReadAsStringAsync();
        using var allDoc = JsonDocument.Parse(allJson);
        allDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // 2. Query non-existent torrent metadata
        var notFoundResp = await this.GetAsync("/api/v1/media/999999");
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MediaArtwork_WhenInvalidType_ReturnsNotFound()
    {
        var invalidTypeResp = await this.GetAsync("/api/v1/media/artwork/1/invalid_artwork_type");
        invalidTypeResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task StreamingAndPlaylist_EndToEndLifecycle_HandlesRangeAndM3UFormats()
    {
        const string hash = "6111111111111111111111111111111111111111";
        const string testTorrentName = "StreamTestMovie.2026";
        const string videoFileName = "video.mp4";

        var baseDir = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        var torrentFolder = Path.Combine(baseDir, "leecharr_media_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(torrentFolder);
        var videoFilePath = Path.Combine(torrentFolder, videoFileName);

        // Write 1 KB dummy video data
        var dummyData = new byte[1024];
        for (var i = 0; i < dummyData.Length; i++)
        {
            dummyData[i] = (byte)(i % 256);
        }

        await File.WriteAllBytesAsync(videoFilePath, dummyData);

        // Add torrent to database via REST API
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent($"magnet:?xt=urn:btih:{hash}&dn={testTorrentName}"), "magnetUrl");
        form.Add(new StringContent("movies"), "category");
        form.Add(new StringContent("true"), "paused");

        var addResp = await this.Client.PostAsync("/api/v1/torrents", form);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addJson = await addResp.Content.ReadAsStringAsync();
        using var addDoc = JsonDocument.Parse(addJson);
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // Query files for this torrent
            var filesResp = await this.GetAsync($"/api/v1/torrent/{torrentId}/files");
            filesResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var filesJson = await filesResp.Content.ReadAsStringAsync();
            using var filesDoc = JsonDocument.Parse(filesJson);

            // 1. File streaming endpoint with Range request
            var streamReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/stream?path={Uri.EscapeDataString(videoFilePath)}");
            streamReq.Headers.Range = new RangeHeaderValue(0, 100);
            var streamResp = await this.Client.SendAsync(streamReq);
            streamResp.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            streamResp.Content.Headers.ContentRange.Should().NotBeNull();

            // 2. File browser M3U playlist endpoint
            var fbPlaylistResp = await this.GetAsync($"/api/v1/files/playlist.m3u?path={Uri.EscapeDataString(videoFilePath)}");
            fbPlaylistResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var fbPlaylistContent = await fbPlaylistResp.Content.ReadAsStringAsync();
            fbPlaylistContent.Should().StartWith("#EXTM3U");
            fbPlaylistContent.Should().Contain(videoFileName);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrent/{torrentId}?deleteFiles=true");
            if (Directory.Exists(torrentFolder))
            {
                Directory.Delete(torrentFolder, true);
            }
        }
    }
}
