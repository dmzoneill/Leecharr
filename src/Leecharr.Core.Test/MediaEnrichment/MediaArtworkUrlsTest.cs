// Copyright (c) FeedItOut. All rights reserved.

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
}
