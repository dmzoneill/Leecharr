// Copyright (c) PlaceholderCompany. All rights reserved.

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
        parsed.V1InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        parsed.DisplayName.Should().Be("Ubuntu.iso");
        parsed.Trackers.Should().HaveCount(2);
        parsed.Trackers.Should().Contain("http://tracker.local/announce");
        parsed.Trackers.Should().Contain("udp://tracker2.local:1337");
    }

    [Test]
    public void Parse_WhenUriContainsFragment_StripsFragmentAndParsesSuccessfully()
    {
        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu.iso#some-fragment-identifier";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        parsed.DisplayName.Should().Be("Ubuntu.iso");
    }

    [Test]
    public void Parse_WhenBase32InfoHash_ConvertsToHex()
    {
        // 32-character base32 hash for 20 bytes (e.g. 20 zero bytes = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA)
        var magnet = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Test";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.DisplayName.Should().Be("Test");
        parsed.InfoHash.Should().Be("0000000000000000000000000000000000000000");
        parsed.V1InfoHash.Should().Be("0000000000000000000000000000000000000000");
    }

    [Test]
    public void Parse_WhenUppercaseKeys_ParsesSuccessfully()
    {
        var magnet = "magnet:?XT=urn:btih:0123456789abcdef0123456789abcdef01234567&DN=Uppercase.iso&TR=http%3A%2F%2Ftracker.local%2Fannounce";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        parsed.DisplayName.Should().Be("Uppercase.iso");
        parsed.Trackers.Should().Contain("http://tracker.local/announce");
    }

    [Test]
    public void Parse_WhenHybridMagnet_SetsBothHashesAndDefaultsToV1()
    {
        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&xt=urn:btmh:1220d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c&dn=Hybrid";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.V1InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        parsed.V2InfoHash.Should().Be("d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c");
        parsed.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        parsed.DisplayName.Should().Be("Hybrid");
    }

    [Test]
    public void Parse_WhenInvalidBase32Chars_ThrowsFormatException()
    {
        var magnet = "magnet:?xt=urn:btih:INVALID_CHARACTERS_NOT_BASE32!&dn=Test";

        Action act = () => MagnetLinkParser.Parse(magnet);
        act.Should().Throw<FormatException>();
    }

    [Test]
    public void Parse_WhenEmpty_ThrowsArgumentException()
    {
        Action act = () => MagnetLinkParser.Parse(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Parse_WhenInvalidPrefix_ThrowsFormatException()
    {
        Action act = () => MagnetLinkParser.Parse("http://invalid-link");
        act.Should().Throw<FormatException>();
    }

    [Test]
    public void Parse_WhenBEP52Multihash_ParsesSuccessfully()
    {
        var magnet = "magnet:?xt=urn:btmh:1220d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c&dn=V2Torrent";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.InfoHash.Should().Be("d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c");
        parsed.V2InfoHash.Should().Be("d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c");
        parsed.DisplayName.Should().Be("V2Torrent");
    }

    [Test]
    public void Parse_WhenBEP52MultihashDirectSha256_ParsesSuccessfully()
    {
        var magnet = "magnet:?xt=urn:btmh:d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c&dn=DirectSha";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.InfoHash.Should().Be("d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c");
        parsed.V2InfoHash.Should().Be("d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c");
        parsed.DisplayName.Should().Be("DirectSha");
    }

    [Test]
    public void Parse_WhenBEP52Multihash52CharBase32_ParsesSuccessfully()
    {
        var magnet = "magnet:?xt=urn:btmh:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Base32V2Torrent";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.InfoHash.Should().Be("0000000000000000000000000000000000000000000000000000000000000000");
        parsed.V2InfoHash.Should().Be("0000000000000000000000000000000000000000000000000000000000000000");
        parsed.DisplayName.Should().Be("Base32V2Torrent");
    }

    [Test]
    public void Parse_WhenBEP52Multihash56CharPaddedBase32_ParsesSuccessfully()
    {
        var magnet = "magnet:?xt=urn:btmh:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA====&dn=PaddedBase32V2Torrent";

        var parsed = MagnetLinkParser.Parse(magnet);

        parsed.InfoHash.Should().Be("0000000000000000000000000000000000000000000000000000000000000000");
        parsed.V2InfoHash.Should().Be("0000000000000000000000000000000000000000000000000000000000000000");
        parsed.DisplayName.Should().Be("PaddedBase32V2Torrent");
    }

    [Test]
    public void NormalizeInfoHash_WhenBase32V2Hash_NormalizesTo64HexChars()
    {
        var normalized = MagnetLinkParser.NormalizeInfoHash("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        normalized.Should().Be("0000000000000000000000000000000000000000000000000000000000000000");

        var normalizedPadded = MagnetLinkParser.NormalizeInfoHash("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA====");
        normalizedPadded.Should().Be("0000000000000000000000000000000000000000000000000000000000000000");
    }

    [TestCase("0123456789abcdef0123456789abcdef01234567")] // 40-hex chars (SHA-1)
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 32-char Base32 (SHA-1)
    [TestCase("1220invalid")]
    [TestCase("1320d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278c")] // 68-char invalid prefix
    [TestCase("d8fadd013a563de212309d361d4810186076b63b6ad3d6293502e645e381278z")] // 64-char invalid hex
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA9")] // 52-char invalid Base32 char
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 56-char unpadded Base32 (35 bytes != 32 bytes)
    public void Parse_WhenBtmhContainsInvalidOrSha1Hash_ThrowsFormatException(string invalidBtmh)
    {
        var magnet = $"magnet:?xt=urn:btmh:{invalidBtmh}&dn=Invalid";

        Action act = () => MagnetLinkParser.Parse(magnet);
        act.Should().Throw<FormatException>();
    }
}
