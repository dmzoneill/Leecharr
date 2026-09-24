// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Leecharr.Api.V1.Subtitles;
using Leecharr.Api.V1.Torrents;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Subtitles;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.Subtitles;

[TestFixture]
public class SubtitleDiscoveryAndStreamingTest
{
    private ISubtitleDiscoveryService discoveryService = null!;
    private ISubtitleConversionService conversionService = null!;
    private ISubtitleEncodingDetector encodingDetector = null!;
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private TorrentController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.discoveryService = new SubtitleDiscoveryService();
        this.encodingDetector = new SubtitleEncodingDetector();
        this.conversionService = new SubtitleConversionService(this.encodingDetector);

        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        var mockParser = Substitute.For<ITorrentFileParser>();
        var mockEnrichment = Substitute.For<IMediaEnrichmentService>();
        var mockTrackers = Substitute.For<ITrackerEntryRepository>();
        var mockSignalR = Substitute.For<IBroadcastSignalRMessage>();
        var mockConfig = Substitute.For<IConfigService>();

        this.controller = new TorrentController(
            this.torrentService,
            this.torrentFileService,
            mockParser,
            mockEnrichment,
            mockTrackers,
            mockSignalR,
            configService: mockConfig,
            subtitleDiscoveryService: this.discoveryService,
            subtitleConversionService: this.conversionService);
    }

    [Test]
    public void IsSubtitleFile_RecognizesCommonExtensions()
    {
        this.discoveryService.IsSubtitleFile("movie.srt").Should().BeTrue();
        this.discoveryService.IsSubtitleFile("movie.vtt").Should().BeTrue();
        this.discoveryService.IsSubtitleFile("movie.sub").Should().BeTrue();
        this.discoveryService.IsSubtitleFile("movie.mp4").Should().BeFalse();
        this.discoveryService.IsSubtitleFile(null!).Should().BeFalse();
    }

    [Test]
    public void IsMediaFile_RecognizesCommonVideoExtensions()
    {
        this.discoveryService.IsMediaFile("movie.mkv").Should().BeTrue();
        this.discoveryService.IsMediaFile("video.mp4").Should().BeTrue();
        this.discoveryService.IsMediaFile("clip.avi").Should().BeTrue();
        this.discoveryService.IsMediaFile("audio.srt").Should().BeFalse();
        this.discoveryService.IsMediaFile(string.Empty).Should().BeFalse();
    }

    [Test]
    public void DiscoverSubtitles_MatchesSubtitleFilesForVideo()
    {
        var videoFile = new TorrentFile
        {
            Id = 1,
            Path = "Blue Mountain State S01E02 Promise Ring 1080p.mkv",
            Size = 2000000000L,
        };

        var allFiles = new List<TorrentFile>
        {
            videoFile,
            new()
            {
                Id = 2,
                Path = "Blue Mountain State S01E02 Promise Ring 1080p.en.srt",
                Size = 50000L,
            },
            new()
            {
                Id = 3,
                Path = "Blue Mountain State S01E02 Promise Ring 1080p.fr.forced.srt",
                Size = 10000L,
            },
            new()
            {
                Id = 4,
                Path = "Blue Mountain State S01E03.en.srt",
                Size = 45000L,
            },
        };

        var discovered = this.discoveryService.DiscoverSubtitles(videoFile, allFiles);

        discovered.Should().NotBeNull();
        discovered.Should().HaveCount(2);

        var enSub = discovered.Find(s => s.Language == "en");
        enSub.Should().NotBeNull();
        enSub!.IsForced.Should().BeFalse();
        enSub.Title.Should().Be("English");

        var frSub = discovered.Find(s => s.Language == "fr");
        frSub.Should().NotBeNull();
        frSub!.IsForced.Should().BeTrue();
        frSub.Title.Should().Be("French (Forced)");
    }

    [Test]
    public void SubtitleConversionService_ConvertsSrtToWebVtt()
    {
        const string srt = "1\n00:00:01,000 --> 00:00:04,000\nHello world!\n\n2\n00:00:05,500 --> 00:00:08,000\nSecond line.\n";
        var vtt = this.conversionService.ConvertToWebVtt(srt, "srt");

        vtt.Should().StartWith("WEBVTT");
        vtt.Should().Contain("00:00:01.000 --> 00:00:04.000");
        vtt.Should().Contain("Hello world!");
        vtt.Should().Contain("00:00:05.500 --> 00:00:08.000");
        vtt.Should().Contain("Second line.");
    }

    [Test]
    public void SubtitleConversionService_WhenAlreadyWebVtt_NormalizesCleanly()
    {
        const string existingVtt = "WEBVTT\n\n00:00:01.000 --> 00:00:04.000\nAlready VTT\n";
        var result = this.conversionService.ConvertToWebVtt(existingVtt, "vtt");

        result.Should().StartWith("WEBVTT");
        result.Should().Contain("Already VTT");
    }

    [Test]
    public void SubtitleEncodingDetector_DetectsUtf8WithBom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'H', (byte)'i' };
        var text = this.encodingDetector.DecodeToUtf8(bytes);
        text.Should().Be("Hi");
    }

    [Test]
    public void StreamFile_WhenTorrentNotFound_ReturnsNotFound()
    {
        this.torrentService.Get(999).Returns((Torrent)null!);

        var result = this.controller.StreamFile(999, 1);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void DownloadFile_WhenTorrentNotFound_ReturnsNotFound()
    {
        this.torrentService.Get(999).Returns((Torrent)null!);

        var result = this.controller.DownloadFile(999, 1);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void StreamFile_WhenFileNotFound_ReturnsNotFound()
    {
        var torrent = new Torrent { Id = 1, Name = "Test" };
        this.torrentService.Get(1).Returns(torrent);
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile>());

        var result = this.controller.StreamFile(1, 42);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void GetFileSubtitles_WhenTorrentNotFound_ReturnsNotFound()
    {
        this.torrentService.Get(999).Returns((Torrent)null!);

        var result = this.controller.GetFileSubtitles(999, 1);
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public void GetFileSubtitles_WhenFileExists_ReturnsDiscoveredSubtitles()
    {
        var torrent = new Torrent { Id = 1, Name = "BMS" };
        var videoFile = new TorrentFile { Id = 10, Path = "BMS.S01E02.mkv" };
        var subFile = new TorrentFile { Id = 11, Path = "BMS.S01E02.en.srt" };

        this.torrentService.Get(1).Returns(torrent);
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile> { videoFile, subFile });

        var response = this.controller.GetFileSubtitles(1, 10);
        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var tracks = okResult.Value.Should().BeAssignableTo<List<SubtitleTrackResource>>().Subject;

        tracks.Should().HaveCount(1);
        tracks[0].TrackId.Should().Be(1);
        tracks[0].FileId.Should().Be(11);
        tracks[0].Language.Should().Be("en");
        tracks[0].Url.Should().Be("/api/v1/torrent/1/files/10/subtitles/1.vtt");
    }

    [Test]
    public void GetSubtitleTrack_WhenTrackNotFound_ReturnsNotFound()
    {
        var torrent = new Torrent { Id = 1, Name = "BMS" };
        var videoFile = new TorrentFile { Id = 10, Path = "BMS.S01E02.mkv" };

        this.torrentService.Get(1).Returns(torrent);
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile> { videoFile });

        var result = this.controller.GetSubtitleTrack(1, 10, "999");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public void GetPlaylistM3u_WhenTorrentNotFound_ReturnsNotFound()
    {
        this.torrentService.Get(999).Returns((Torrent)null!);

        var result = this.controller.GetPlaylistM3u(999, 1);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public void GetPlaylistM3u_WhenFileExists_ReturnsM3uFile()
    {
        var torrent = new Torrent { Id = 1, Name = "BMS" };
        var videoFile = new TorrentFile { Id = 10, Path = "BMS.S01E02.mkv" };

        this.torrentService.Get(1).Returns(torrent);
        this.torrentFileService.GetFiles(1).Returns(new List<TorrentFile> { videoFile });

        var result = this.controller.GetPlaylistM3u(1, 10);
        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;

        fileResult.ContentType.Should().Be("audio/x-mpegurl");
        fileResult.FileDownloadName.Should().Be("BMS.S01E02.mkv.m3u");
        var content = Encoding.UTF8.GetString(fileResult.FileContents);
        content.Should().StartWith("#EXTM3U");
        content.Should().Contain("/api/v1/torrent/1/files/10/stream");
    }
}
