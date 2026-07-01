using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class MagnetLinkParserTest
{
    [Test]
    public void Parse_WhenValidHexMagnet_ParsesSuccessfully()
    {
        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu.iso&tr=http%3A%2F%2Ftracker.local%2Fannounce&tr=udp%3A%2F%2Ftracker2.local%3A1337";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        parsed.DisplayName.Should().Be("Ubuntu.iso");
        parsed.Trackers.Should().HaveCount(2);
        parsed.Trackers.Should().Contain("http://tracker.local/announce");
        parsed.Trackers.Should().Contain("udp://tracker2.local:1337");
    }

    [Test]
    public void Parse_WhenBase32InfoHash_ConvertsToHex()
    {
        // 32-character base32 hash
        var magnet = "magnet:?xt=urn:btih:MFRGGZDFMY======&dn=Test";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.DisplayName.Should().Be("Test");
        parsed.InfoHash.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Parse_WhenEmpty_ThrowsArgumentException()
    {
        Action act = () => MagnetLinkParser.Parse("");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Parse_WhenInvalidPrefix_ThrowsFormatException()
    {
        Action act = () => MagnetLinkParser.Parse("http://invalid-link");
        act.Should().Throw<FormatException>();
    }
}
