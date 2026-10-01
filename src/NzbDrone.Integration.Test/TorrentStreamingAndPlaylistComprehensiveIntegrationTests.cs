// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentStreamingAndPlaylistComprehensiveIntegrationTests : IntegrationTestBase
{
    private string testDirectory = null!;
    private Torrent testTorrent = null!;
    private TorrentFile videoFile = null!;
    private TorrentFile subFile = null!;

    [SetUp]
    public void SetUp()
    {
        var baseDir = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        this.testDirectory = Path.Combine(baseDir, "stream_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.testDirectory);

        // Create sample files on disk
        var videoDiskPath = Path.Combine(this.testDirectory, "test_sample_video.mp4");
        File.WriteAllBytes(videoDiskPath, new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D });

        var subDiskPath = Path.Combine(this.testDirectory, "test_sample_video.en.srt");
        File.WriteAllText(subDiskPath, "1\n00:00:01,000 --> 00:00:04,000\nHello streaming world!\n");

        var audioDiskPath = Path.Combine(this.testDirectory, "test_audio.mp3");
        File.WriteAllBytes(audioDiskPath, new byte[] { 0xFF, 0xFB, 0x90, 0x64 });

        var services = GlobalSetup.Factory.Services;
        var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
        var torrentFileRepo = (ITorrentFileRepository)services.GetService(typeof(ITorrentFileRepository))!;

        this.testTorrent = new Torrent
        {
            Name = "StreamingTestMovie",
            InfoHash = "beefbeefbeefbeefbeefbeefbeefbeefbeefbeef",
            SavePath = this.testDirectory,
            Status = TorrentStatus.Paused,
            TotalSize = 1024,
            Progress = 1.0,
            DateAdded = DateTime.UtcNow,
        };
        torrentRepo.Insert(this.testTorrent);

        this.videoFile = new TorrentFile
        {
            TorrentId = this.testTorrent.Id,
            Path = "test_sample_video.mp4",
            Size = 512,
            PieceOffset = 0,
            PieceCount = 2,
            Priority = 3,
            Progress = 1.0,
            IsPaddingFile = false,
        };
        torrentFileRepo.Insert(this.videoFile);

        this.subFile = new TorrentFile
        {
            TorrentId = this.testTorrent.Id,
            Path = "test_sample_video.en.srt",
            Size = 64,
            PieceOffset = 2,
            PieceCount = 1,
            Priority = 3,
            Progress = 1.0,
            IsPaddingFile = false,
        };
        torrentFileRepo.Insert(this.subFile);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            var services = GlobalSetup.Factory.Services;
            var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
            var torrentFileRepo = (ITorrentFileRepository)services.GetService(typeof(ITorrentFileRepository))!;

            if (this.videoFile != null)
            {
                torrentFileRepo.Delete(this.videoFile.Id);
            }

            if (this.subFile != null)
            {
                torrentFileRepo.Delete(this.subFile.Id);
            }

            if (this.testTorrent != null)
            {
                torrentRepo.Delete(this.testTorrent.Id);
            }
        }
        catch
        {
        }

        if (Directory.Exists(this.testDirectory))
        {
            try
            {
                Directory.Delete(this.testDirectory, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task TorrentPlaylists_M3uEndpoints_ReturnValidM3uContent()
    {
        // 1. Torrent-level GET & HEAD stream.m3u
        var torrentM3uResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/stream.m3u");
        torrentM3uResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var torrentM3uContent = await torrentM3uResp.Content.ReadAsStringAsync();
        torrentM3uContent.Should().Contain("#EXTM3U");
        torrentM3uContent.Should().Contain("test_sample_video.mp4");

        var torrentHeadM3u = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/torrents/{this.testTorrent.Id}/stream.m3u");
        var headResp = await this.Client.SendAsync(torrentHeadM3u);
        headResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Torrent-level playlist.m3u
        var playlistResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/playlist.m3u");
        playlistResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var playlistContent = await playlistResp.Content.ReadAsStringAsync();
        playlistContent.Should().Contain("#EXTM3U");

        // 3. File-level GET & HEAD stream.m3u
        var fileM3uResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/stream.m3u");
        fileM3uResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fileM3uContent = await fileM3uResp.Content.ReadAsStringAsync();
        fileM3uContent.Should().Contain("#EXTM3U");
        fileM3uContent.Should().Contain($"/api/v1/torrent/{this.testTorrent.Id}/files/{this.videoFile.Id}/stream");

        var fileHeadM3u = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/stream.m3u");
        var fileHeadResp = await this.Client.SendAsync(fileHeadM3u);
        fileHeadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. File-level playlist.m3u
        var filePlaylistResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/playlist.m3u");
        filePlaylistResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TorrentStreamingAndDownload_ServesPhysicalContentWithRangeHeaders()
    {
        // 1. Stream file: GET & HEAD /stream
        var streamResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/stream");
        streamResp.StatusCode.Should().Be(HttpStatusCode.OK);
        streamResp.Content.Headers.ContentType?.MediaType.Should().Be("video/mp4");
        var streamBytes = await streamResp.Content.ReadAsByteArrayAsync();
        streamBytes.Length.Should().BeGreaterThan(0);

        var streamHeadReq = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/stream");
        var streamHeadResp = await this.Client.SendAsync(streamHeadReq);
        streamHeadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Download file: GET & HEAD /download
        var dlResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/download");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        dlResp.Content.Headers.ContentDisposition?.FileName.Should().Contain("test_sample_video.mp4");

        var dlHeadReq = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/download");
        var dlHeadResp = await this.Client.SendAsync(dlHeadReq);
        dlHeadResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TorrentSubtitles_DiscoveryAndVttConversion_Succeeds()
    {
        // 1. Discovery at torrent level: GET /subtitles
        var torrentSubsResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/subtitles");
        torrentSubsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var subsJson = await torrentSubsResp.Content.ReadAsStringAsync();
        using var subsDoc = JsonDocument.Parse(subsJson);
        subsDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        // 2. Discovery at file level: GET /files/{fileId}/subtitles
        var fileSubsResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/subtitles");
        fileSubsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. VTT subtitle conversion: GET /subtitles/{trackId}.vtt
        var vttResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/subtitles/1.vtt");
        vttResp.StatusCode.Should().Be(HttpStatusCode.OK);
        vttResp.Content.Headers.ContentType?.MediaType.Should().Be("text/vtt");
        var vttContent = await vttResp.Content.ReadAsStringAsync();
        vttContent.Should().Contain("WEBVTT");
        vttContent.Should().Contain("Hello streaming world!");

        // 4. VTT subtitle conversion at file level
        var fileVttResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/files/{this.videoFile.Id}/subtitles/1.vtt");
        fileVttResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fileVttContent = await fileVttResp.Content.ReadAsStringAsync();
        fileVttContent.Should().Contain("WEBVTT");

        // 5. Non-existent subtitle track returns 404
        var notFoundSubResp = await this.Client.GetAsync($"/api/v1/torrents/{this.testTorrent.Id}/subtitles/99999.vtt");
        notFoundSubResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task FileBrowser_StreamingAndPreview_EndpointsOperateSuccessfully()
    {
        var videoPath = Path.Combine(this.testDirectory, "test_sample_video.mp4");
        var audioPath = Path.Combine(this.testDirectory, "test_audio.mp3");
        var srtPath = Path.Combine(this.testDirectory, "test_sample_video.en.srt");

        // 1. stream.m3u & playlist.m3u for file
        var m3uResp = await this.Client.GetAsync($"/api/v1/files/stream.m3u?path={Uri.EscapeDataString(videoPath)}");
        m3uResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var m3uContent = await m3uResp.Content.ReadAsStringAsync();
        m3uContent.Should().Contain("#EXTM3U");
        m3uContent.Should().Contain("test_sample_video.mp4");

        var headM3uReq = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/files/stream.m3u?path={Uri.EscapeDataString(videoPath)}");
        var headM3uResp = await this.Client.SendAsync(headM3uReq);
        headM3uResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var playlistResp = await this.Client.GetAsync($"/api/v1/files/playlist.m3u?path={Uri.EscapeDataString(videoPath)}");
        playlistResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. preview endpoints
        var videoPrevResp = await this.Client.GetAsync($"/api/v1/files/preview?path={Uri.EscapeDataString(videoPath)}");
        videoPrevResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var videoPrevJson = await videoPrevResp.Content.ReadAsStringAsync();
        using var vDoc = JsonDocument.Parse(videoPrevJson);
        vDoc.RootElement.GetProperty("type").GetString().Should().Be("video");

        var audioPrevResp = await this.Client.GetAsync($"/api/v1/files/preview?path={Uri.EscapeDataString(audioPath)}");
        audioPrevResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var audioPrevJson = await audioPrevResp.Content.ReadAsStringAsync();
        using var aDoc = JsonDocument.Parse(audioPrevJson);
        aDoc.RootElement.GetProperty("type").GetString().Should().Be("audio");

        var textPrevResp = await this.Client.GetAsync($"/api/v1/files/preview?path={Uri.EscapeDataString(srtPath)}");
        textPrevResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var textPrevJson = await textPrevResp.Content.ReadAsStringAsync();
        using var tDoc = JsonDocument.Parse(textPrevJson);
        tDoc.RootElement.GetProperty("type").GetString().Should().Be("text");

        // 3. Error handling: empty path -> 400, nonexistent -> 404
        var emptyPathResp = await this.Client.GetAsync("/api/v1/files/stream.m3u?path=");
        emptyPathResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var notFoundPathResp = await this.Client.GetAsync($"/api/v1/files/stream.m3u?path={Uri.EscapeDataString(Path.Combine(this.testDirectory, "missing.mp4"))}");
        notFoundPathResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
