using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class MagnetLinkParserTest
{
    [Test]
    public void should_parse_hex_magnet_link()
    {
        const string magnet = "magnet:?xt=urn:btih:3b245504fb5fec2147ac37033dc1514c28bc23b6&dn=Ubuntu+22.04&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce";

        var parsed = MagnetLinkParser.Parse(magnet);

        Assert.That(parsed.InfoHash, Is.EqualTo("3b245504fb5fec2147ac37033dc1514c28bc23b6"));
        Assert.That(parsed.DisplayName, Is.EqualTo("Ubuntu 22.04"));
        Assert.That(parsed.Trackers.Count, Is.EqualTo(1));
        Assert.That(parsed.Trackers.First(), Is.EqualTo("udp://tracker.opentrackr.org:1337/announce"));
    }

    [Test]
    public void should_parse_base32_magnet_link()
    {
        // 32-char Base32 infohash
        const string magnet = "magnet:?xt=urn:btih:HMJFLRHPX7WCEIP4G4BT3QKRJQU3YI5W&dn=Test";

        var parsed = MagnetLinkParser.Parse(magnet);

        Assert.That(parsed.InfoHash, Is.Not.Null);
        Assert.That(parsed.InfoHash.Length, Is.EqualTo(40));
        Assert.That(parsed.DisplayName, Is.EqualTo("Test"));
    }
}
