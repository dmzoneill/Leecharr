// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using MonoTorrent.BEncoding;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentFileParserTest
{
    private TorrentFileParser parser = null!;

    [SetUp]
    public void SetUp()
    {
        this.parser = new TorrentFileParser();
    }

    private static byte[] CreateTorrentBytes(Action<BEncodedDictionary> customizeInfo = null)
    {
        var pieceLength = 16384;
        var pieces = new byte[20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i + 1);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString("sample.iso") },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "length", new BEncodedNumber(16384) },
        };

        customizeInfo?.Invoke(infoDict);

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
        };

        return rootDict.Encode();
    }

    [Test]
    public void Parse_WhenPrivateBNumberIsOne_ReturnsIsPrivateTrue()
    {
        var bytes = CreateTorrentBytes(info => info["private"] = new BEncodedNumber(1));

        var parsed = this.parser.Parse(bytes);

        parsed.IsPrivate.Should().BeTrue();
    }

    [Test]
    public void Parse_WhenPrivateBStringIsOne_ReturnsIsPrivateTrue()
    {
        var bytes = CreateTorrentBytes(info => info["private"] = new BEncodedString("1"));

        var parsed = this.parser.Parse(bytes);

        parsed.IsPrivate.Should().BeTrue();
    }

    [Test]
    public void Parse_WhenPrivateBNumberIsZero_ReturnsIsPrivateFalse()
    {
        var bytes = CreateTorrentBytes(info => info["private"] = new BEncodedNumber(0));

        var parsed = this.parser.Parse(bytes);

        parsed.IsPrivate.Should().BeFalse();
    }

    [Test]
    public void Parse_WhenPrivateFlagIsMissing_ReturnsIsPrivateFalse()
    {
        var bytes = CreateTorrentBytes();

        var parsed = this.parser.Parse(bytes);

        parsed.IsPrivate.Should().BeFalse();
    }
}
