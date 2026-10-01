// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using MonoTorrent.BEncoding;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentFileParserComprehensiveIntegrationTests : IntegrationTestBase
{
    private TorrentFileParser parser;

    [SetUp]
    public void SetUp()
    {
        this.parser = new TorrentFileParser();
    }

    private static byte[] CreateTorrentBytes(Action<BEncodedDictionary> customizeInfo = null, Action<BEncodedDictionary> customizeRoot = null)
    {
        var pieceLength = 16384;
        var pieces = new byte[20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i + 1);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString("sample_file.mkv") },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "length", new BEncodedNumber(16384) },
        };

        customizeInfo?.Invoke(infoDict);

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
            { "comment", new BEncodedString("Test comment") },
            { "created by", new BEncodedString("Leecharr Test Suite") },
            { "creation date", new BEncodedNumber(1700000000) },
        };

        customizeRoot?.Invoke(rootDict);

        return rootDict.Encode();
    }

    [Test]
    public void TorrentFileParser_SingleFileTorrent_ParsesAccurately()
    {
        var bytes = CreateTorrentBytes(info =>
        {
            info["private"] = new BEncodedNumber(1);
        });

        var parsed = this.parser.Parse(bytes);

        parsed.Should().NotBeNull();
        parsed.Name.Should().Be("sample_file.mkv");
        parsed.TotalSize.Should().Be(16384);
        parsed.PieceLength.Should().Be(16384);
        parsed.PieceCount.Should().Be(1);
        parsed.IsPrivate.Should().BeTrue();
        parsed.AnnounceUrl.Should().Be("http://tracker.example.com/announce");
        parsed.Comment.Should().Be("Test comment");
        parsed.CreatedBy.Should().Be("Leecharr Test Suite");
        parsed.InfoHash.Should().NotBeNullOrWhiteSpace();
        parsed.Files.Should().HaveCount(1);
        parsed.Files[0].Path.Should().Be("sample_file.mkv");
    }

    [Test]
    public void TorrentFileParser_MultiFileTorrent_ParsesFilesAndAnnounceList()
    {
        var pieceLength = 32768;
        var pieces = new byte[40]; // 2 pieces

        var filesList = new BEncodedList
        {
            new BEncodedDictionary
            {
                { "length", new BEncodedNumber(20000) },
                { "path", new BEncodedList { new BEncodedString("subfolder"), new BEncodedString("video1.mp4") } },
            },
            new BEncodedDictionary
            {
                { "length", new BEncodedNumber(45536) },
                { "path", new BEncodedList { new BEncodedString("subfolder"), new BEncodedString("video2.mp4") } },
            },
        };

        var announceList = new BEncodedList
        {
            new BEncodedList { new BEncodedString("http://tracker1.tier.com/announce"), new BEncodedString("http://tracker1b.tier.com/announce") },
            new BEncodedList { new BEncodedString("udp://tracker2.tier.com:1337/announce") },
        };

        var bytes = CreateTorrentBytes(
            info =>
            {
                info.Remove("length");
                info["name"] = new BEncodedString("MultiFileRelease");
                info["piece length"] = new BEncodedNumber(pieceLength);
                info["pieces"] = new BEncodedString(pieces);
                info["files"] = filesList;
            },
            root =>
            {
                root["announce-list"] = announceList;
            });

        var parsed = this.parser.Parse(bytes);

        parsed.Name.Should().Be("MultiFileRelease");
        parsed.TotalSize.Should().Be(65536);
        parsed.PieceCount.Should().Be(2);
        parsed.Files.Should().HaveCount(2);
        parsed.Files[0].Path.Should().Contain("video1.mp4");
        parsed.Files[1].Path.Should().Contain("video2.mp4");
        parsed.AnnounceList.Should().NotBeNull();
        parsed.AnnounceList.Should().HaveCount(2);
    }

    [Test]
    public void TorrentFileParser_CorruptedAndInvalidBytes_ThrowsValidationException()
    {
        // 1. Empty bytes
        Action actEmpty = () => this.parser.Parse(Array.Empty<byte>());
        actEmpty.Should().Throw<InvalidTorrentFileException>();

        // 2. Corrupted / incomplete bencode
        var incompleteBytes = Encoding.UTF8.GetBytes("d8:announce20:http://test.com/4:info");
        Action actIncomplete = () => this.parser.Parse(incompleteBytes);
        actIncomplete.Should().Throw<InvalidTorrentFileException>();

        // 3. Missing info dictionary
        var missingInfoDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://test.com/announce") },
        };
        Action actMissingInfo = () => this.parser.Parse(missingInfoDict.Encode());
        actMissingInfo.Should().Throw<InvalidTorrentFileException>();

        // 4. Missing pieces
        var missingPieces = CreateTorrentBytes(info => info.Remove("pieces"));
        Action actMissingPieces = () => this.parser.Parse(missingPieces);
        actMissingPieces.Should().Throw<InvalidTorrentFileException>();
    }
}
