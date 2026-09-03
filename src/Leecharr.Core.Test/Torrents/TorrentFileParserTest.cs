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
    [TestCase("dir/sub")]
    [TestCase(@"dir\sub")]
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
}
