// Copyright (c) FeedItOut. All rights reserved.

using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class MediaArtworkUrlsTest
{
    [Test]
    public void ToPublicUrl_WhenLocalPathPresent_ReturnsArtworkEndpoint()
    {
        var url = MediaArtworkUrls.ToPublicUrl(
            99,
            "/cache/99/banner.jpg",
            "http://127.0.0.1:8989/MediaCover/1/banner.jpg?apikey=secret",
            "banner");

        url.Should().Be("/api/v1/media/artwork/99/banner");
    }

    [Test]
    public void ToPublicUrl_WhenOnlyRemoteUrl_StripsApiKeyQuery()
    {
        var url = MediaArtworkUrls.ToPublicUrl(
            0,
            null,
            "http://127.0.0.1:8989/MediaCover/1/banner.jpg?apikey=secret&foo=bar",
            "banner");

        url.Should().Be("http://127.0.0.1:8989/MediaCover/1/banner.jpg?foo=bar");
    }

    [Test]
    public void RedactApiKeyQuery_WhenOnlyApiKeyParam_ReturnsPathWithoutQuery()
    {
        MediaArtworkUrls.RedactApiKeyQuery("http://127.0.0.1:7878/MediaCover/1/fanart.jpg?apikey=test")
            .Should().Be("http://127.0.0.1:7878/MediaCover/1/fanart.jpg");
    }

    [Test]
    public void FindCachedFile_WhenPosterExists_ReturnsCachePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "leecharr-artwork-" + Path.GetRandomFileName());
        var poster = Path.Combine(root, "MediaCache", "42", "poster.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(poster)!);
        File.WriteAllBytes(poster, [0xFF, 0xD8, 0xFF, 0xD9]);

        try
        {
            MediaArtworkUrls.FindCachedFile(root, 42, "poster").Should().Be(poster);
            MediaArtworkUrls.FindCachedFile(root, 42, "backdrop").Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
