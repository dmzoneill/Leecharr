// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using MonoTorrent.BEncoding;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
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

    private static byte[] CreateMultiFileTorrentBytes(string rootName, params (long Length, string[] Path)[] files)
    {
        var pieceLength = 16384;
        var pieces = new byte[20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i + 1);
        }

        var filesList = new BEncodedList();
        foreach (var file in files)
        {
            var pathList = new BEncodedList();
            foreach (var part in file.Path)
            {
                pathList.Add(new BEncodedString(part));
            }

            var fileDict = new BEncodedDictionary
            {
                { "length", new BEncodedNumber(file.Length) },
                { "path", pathList },
            };
            filesList.Add(fileDict);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString(rootName) },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "files", filesList },
        };

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

    [TestCase("..")]
    [TestCase(".")]
    [TestCase("../evil")]
    [TestCase(@"..\evil")]
    [TestCase("/etc/cron.d/payload.sh")]
    [TestCase(@"C:\Windows\System32")]
    [TestCase("sample\0.iso")]
    [TestCase("   ")]
    public void Parse_WhenSingleFileTorrentNameIsInvalidOrTraverses_ThrowsInvalidTorrentFileException(string badName)
    {
        var bytes = CreateTorrentBytes(info => info["name"] = new BEncodedString(badName));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>();
    }

    [TestCase("..")]
    [TestCase(".")]
    [TestCase("/root")]
    [TestCase("root\0name")]
    [TestCase("   ")]
    public void Parse_WhenMultiFileTorrentRootNameIsInvalidOrTraverses_ThrowsInvalidTorrentFileException(string badRoot)
    {
        var bytes = CreateMultiFileTorrentBytes(badRoot, (1024, new[] { "file.txt" }));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>();
    }

    [Test]
    public void Parse_WhenSingleFileTorrentNameContainsSlashes_SanitizesPathAndPreservesName()
    {
        var bytes = CreateTorrentBytes(info => info["name"] = new BEncodedString("AC/DC - Back in Black.mp3"));

        var parsed = this.parser.Parse(bytes);

        parsed.Name.Should().Be("AC/DC - Back in Black.mp3");
        parsed.Files.Should().HaveCount(1);
        parsed.Files[0].Path.Should().Be("AC_DC - Back in Black.mp3");
    }

    [Test]
    public void Parse_WhenMultiFileTorrentRootNameContainsSlashes_ParsesSuccessfully()
    {
        var bytes = CreateMultiFileTorrentBytes("Show S01 [H.264/AAC]", (1024, new[] { "Episode 1.mkv" }));

        var parsed = this.parser.Parse(bytes);

        parsed.Name.Should().Be("Show S01 [H.264/AAC]");
        parsed.Files.Should().HaveCount(1);
        parsed.Files[0].Path.Should().Be("Episode 1.mkv");
    }

    [Test]
    public void Parse_WhenMultiFilePathContainsTraversalSequence_ThrowsInvalidTorrentFileException()
    {
        var bytes = CreateMultiFileTorrentBytes("MyTorrent", (1024, new[] { "..", "..", "etc", "cron.d", "payload.sh" }));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*directory traversal*");
    }

    [Test]
    public void Parse_WhenMultiFilePathContainsDotComponent_ThrowsInvalidTorrentFileException()
    {
        var bytes = CreateMultiFileTorrentBytes("MyTorrent", (1024, new[] { ".", "file.txt" }));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*directory traversal*");
    }

    [Test]
    public void Parse_WhenMultiFilePathContainsNullByte_ThrowsInvalidTorrentFileException()
    {
        var bytes = CreateMultiFileTorrentBytes("MyTorrent", (1024, new[] { "folder", "evil\0.txt" }));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*null byte*");
    }

    [TestCase("/etc/shadow")]
    [TestCase(@"\Windows\System32")]
    [TestCase(@"C:\file.txt")]
    public void Parse_WhenMultiFilePathComponentIsAbsolute_ThrowsInvalidTorrentFileException(string absolutePart)
    {
        var bytes = CreateMultiFileTorrentBytes("MyTorrent", (1024, new[] { absolutePart, "file.txt" }));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*absolute path*");
    }

    [Test]
    public void Parse_WhenMultiFilePathComponentContainsTraversalSegment_ThrowsInvalidTorrentFileException()
    {
        var bytes = CreateMultiFileTorrentBytes("MyTorrent", (1024, new[] { "sub/../escaped", "file.txt" }));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*directory traversal*");
    }

    [Test]
    public void Parse_WhenMultiFileTorrentHasValidPaths_ParsesSuccessfully()
    {
        var bytes = CreateMultiFileTorrentBytes(
            "MyTorrent",
            (1024, new[] { "Season 1", "Episode 1.mkv" }),
            (2048, new[] { "Season 1", "Episode 2.mkv" }));

        var parsed = this.parser.Parse(bytes);

        parsed.Name.Should().Be("MyTorrent");
        parsed.Files.Should().HaveCount(2);
        parsed.Files[0].Path.Should().Be("Season 1/Episode 1.mkv");
        parsed.Files[0].Size.Should().Be(1024);
        parsed.Files[1].Path.Should().Be("Season 1/Episode 2.mkv");
        parsed.Files[1].Size.Should().Be(2048);
        parsed.TotalSize.Should().Be(3072);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-16384)]
    public void Parse_WhenPieceLengthIsZeroOrNegative_ThrowsInvalidTorrentFileException(long badPieceLength)
    {
        var bytes = CreateTorrentBytes(info => info["piece length"] = new BEncodedNumber(badPieceLength));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*Piece length must be a positive integer.*");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(19)]
    [TestCase(21)]
    [TestCase(39)]
    public void Parse_WhenPiecesHashStringLengthIsNotMultipleOf20_ThrowsInvalidTorrentFileException(int invalidLength)
    {
        var bytes = CreateTorrentBytes(info => info["pieces"] = new BEncodedString(new byte[invalidLength]));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*Pieces hash string length must be a non-zero multiple of 20.*");
    }

    [Test]
    public void Parse_WhenSingleFileLengthIsNegative_ThrowsInvalidTorrentFileException()
    {
        var bytes = CreateTorrentBytes(info => info["length"] = new BEncodedNumber(-1024));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*File length cannot be negative.*");
    }

    [Test]
    public void Parse_WhenMultiFileLengthIsNegative_ThrowsInvalidTorrentFileException()
    {
        var bytes = CreateMultiFileTorrentBytes("MyTorrent", (-500, new[] { "sub", "corrupt.txt" }));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*File length cannot be negative.*");
    }

    [Test]
    public void Parse_WhenPieceCountDoesNotMatchTotalFileSize_ThrowsInvalidTorrentFileException()
    {
        // 16384 bytes with pieceLength 16384 expects exactly 1 piece (20 bytes).
        // Providing 40 bytes (2 pieces) causes a mismatch.
        var bytes = CreateTorrentBytes(info => info["pieces"] = new BEncodedString(new byte[40]));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*Piece count does not match total file size.*");
    }
}
