// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.Media;
using Leecharr.Api.V1.Torrents;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentResourceMapperTest
{
    [Test]
    public void ToResource_WithLocalArtworkPath_MapsToArtworkApiEndpointWithoutDiskChecks()
    {
        var torrent = new Torrent
        {
            Id = 123,
            Name = "Sample Movie (2024)",
            InfoHash = "abcdef1234567890abcdef1234567890abcdef12",
        };

        var metadata = new TorrentMediaMetadata
        {
            TorrentId = 123,
            Title = "Sample Movie",
            PosterLocalPath = "/nonexistent/path/to/poster.jpg",
            BackdropLocalPath = "/nonexistent/path/to/backdrop.jpg",
            PosterUrl = "https://image.tmdb.org/poster.jpg",
            BackdropUrl = "https://image.tmdb.org/backdrop.jpg",
        };

        var resource = TorrentResourceMapper.ToResource(torrent, metadata: metadata);

        resource.Should().NotBeNull();
        resource.PosterUrl.Should().Be("/api/v1/media/artwork/123/poster");
        resource.BackdropUrl.Should().Be("/api/v1/media/artwork/123/backdrop");
    }

    [Test]
    public void ToResource_WithoutLocalArtworkPath_FallsBackToRemoteUrl()
    {
        var torrent = new Torrent
        {
            Id = 124,
            Name = "Sample Movie 2",
            InfoHash = "abcdef1234567890abcdef1234567890abcdef13",
        };

        var metadata = new TorrentMediaMetadata
        {
            TorrentId = 124,
            Title = "Sample Movie 2",
            PosterLocalPath = null,
            BackdropLocalPath = null,
            PosterUrl = "https://image.tmdb.org/poster2.jpg",
            BackdropUrl = "https://image.tmdb.org/backdrop2.jpg",
        };

        var resource = TorrentResourceMapper.ToResource(torrent, metadata: metadata);

        resource.Should().NotBeNull();
        resource.PosterUrl.Should().Be("https://image.tmdb.org/poster2.jpg");
        resource.BackdropUrl.Should().Be("https://image.tmdb.org/backdrop2.jpg");
    }

    [Test]
    public void MediaMetadataResourceMapper_WithLocalArtwork_MapsToArtworkApiEndpoint()
    {
        var metadata = new TorrentMediaMetadata
        {
            Id = 1,
            TorrentId = 456,
            Title = "Show Title",
            PosterLocalPath = "/nonexistent/poster.jpg",
            BackdropLocalPath = "/nonexistent/backdrop.jpg",
        };

        var resource = MediaMetadataResourceMapper.ToResource(metadata);

        resource.Should().NotBeNull();
        resource.PosterUrl.Should().Be("/api/v1/media/artwork/456/poster");
        resource.BackdropUrl.Should().Be("/api/v1/media/artwork/456/backdrop");
    }

    [Test]
    [TestCase("https://private.tracker.org/announce?passkey=secret123456", "https://private.tracker.org/announce?passkey=********")]
    [TestCase("https://private.tracker.org/announce?authkey=secret123456", "https://private.tracker.org/announce?authkey=********")]
    [TestCase("https://private.tracker.org/announce?torrent_pass=secret123456", "https://private.tracker.org/announce?torrent_pass=********")]
    [TestCase("https://private.tracker.org/announce?token=secret123456789012", "https://private.tracker.org/announce?token=********")]
    [TestCase("https://gazelle.tracker.org/0123456789abcdef0123456789abcdef/announce", "https://gazelle.tracker.org/********/announce")]
    [TestCase("https://ptp.tracker.org/announce/0123456789abcdef0123456789abcdef", "https://ptp.tracker.org/announce/********")]
    [TestCase("https://unit3d.tracker.org/announce/MySecretToken123456", "https://unit3d.tracker.org/announce/********")]
    [TestCase("http://user:secret123456@tracker.site/announce", "http://user:********@tracker.site/announce")]
    public void ToResource_WhenTrackerUrlContainsPasskey_SanitizesTrackerUrlAndTrackersList(string rawUrl, string expectedSanitized)
    {
        var torrent = new Torrent
        {
            Id = 200,
            Name = "Private Torrent",
            TrackerUrl = rawUrl,
        };

        var resource = TorrentResourceMapper.ToResource(torrent);

        resource.Should().NotBeNull();
        resource.TrackerUrl.Should().Be(expectedSanitized);
        resource.TrackerUrl.Should().NotContain("secret");
        resource.TrackerUrl.Should().NotContain("0123456789abcdef0123456789abcdef");
        resource.Trackers.Should().ContainSingle();
        resource.Trackers[0].Should().Be(expectedSanitized);
    }

    [Test]
    [TestCase("udp://tracker.opentrackr.org:1337/announce")]
    [TestCase("http://tracker.files.fm:6969/announce")]
    [TestCase("http://tracker.example.com/announce.php")]
    public void ToResource_WhenTrackerUrlIsPublic_PreservesTrackerUrl(string publicUrl)
    {
        var torrent = new Torrent
        {
            Id = 201,
            Name = "Public Torrent",
            TrackerUrl = publicUrl,
        };

        var resource = TorrentResourceMapper.ToResource(torrent);

        resource.Should().NotBeNull();
        resource.TrackerUrl.Should().Be(publicUrl);
        resource.Trackers.Should().ContainSingle();
        resource.Trackers[0].Should().Be(publicUrl);
    }

    [Test]
    public void ToResource_WithActiveDownloadTask_OverridesTelemetryFromTask()
    {
        var torrent = new Torrent
        {
            Id = 300,
            Name = "Live Sync Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.1,
            Downloaded = 1000,
            DownloadSpeed = 500,
            UploadSpeed = 100,
            Seeders = 1,
            Leechers = 2,
        };

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.Progress.Returns(0.45);
        mockTask.DownloadedBytes.Returns(45000);
        mockTask.DownloadSpeed.Returns(10000);
        mockTask.UploadSpeed.Returns(2000);
        mockTask.ConnectedSeeders.Returns(12);
        mockTask.ConnectedLeechers.Returns(8);
        mockTask.Status.Returns(TorrentStatus.Downloading);

        var resource = TorrentResourceMapper.ToResource(torrent, task: mockTask);

        resource.Should().NotBeNull();
        resource.Progress.Should().Be(0.45);
        resource.Downloaded.Should().Be(45000);
        resource.DownloadSpeed.Should().Be(10000);
        resource.UploadSpeed.Should().Be(2000);
        resource.Seeders.Should().Be(12);
        resource.Leechers.Should().Be(8);
    }
}
