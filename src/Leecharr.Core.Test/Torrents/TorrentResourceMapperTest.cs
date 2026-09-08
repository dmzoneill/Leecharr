// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.Media;
using Leecharr.Api.V1.Torrents;
using NUnit.Framework;
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
}
