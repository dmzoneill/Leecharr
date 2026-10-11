// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.Media;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class TorrentMediaCastCodecTest
{
    [Test]
    public void Serialize_ThenParse_PreservesNamesWithCommas()
    {
        var cast = new[] { "Downey, Robert", "Tom Hanks as Captain, US Army" };

        var stored = TorrentMediaCastCodec.Serialize(cast);
        var parsed = TorrentMediaCastCodec.Parse(stored);

        parsed.Should().ContainInOrder(cast);
    }

    [Test]
    public void Parse_LegacyCommaSeparated_StillSplitsEntries()
    {
        var parsed = TorrentMediaCastCodec.Parse("Russell Crowe, Joaquin Phoenix");

        parsed.Should().ContainInOrder("Russell Crowe", "Joaquin Phoenix");
    }

    [Test]
    public void MediaMetadataResourceMapper_Parse_UsesCodecForCommasInNames()
    {
        var metadata = new TorrentMediaMetadata
        {
            TorrentId = 7,
            Cast = TorrentMediaCastCodec.Serialize(new[] { "Downey, Robert", "Cillian Murphy" }),
        };

        var resource = MediaMetadataResourceMapper.ToResource(metadata);

        resource.Cast.Should().ContainInOrder("Downey, Robert", "Cillian Murphy");
    }
}
