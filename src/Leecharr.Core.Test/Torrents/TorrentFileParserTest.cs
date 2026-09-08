// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Text;
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

    [TestCase(1000)]
    [TestCase(17000)]
    [TestCase(8192)] // < 16 KiB
    [TestCase(134217728)] // > 64 MiB (128 MiB)
    public void Parse_WhenPieceLengthOutsideBoundsOrNotPowerOfTwo_ThrowsInvalidTorrentFileException(long badPieceLength)
    {
        var bytes = CreateTorrentBytes(info => info["piece length"] = new BEncodedNumber(badPieceLength));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*Piece length must be a power of 2 between 16 KiB and 64 MiB.*");
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
        var bytes = CreateTorrentBytes(info => info["pieces"] = new BEncodedString(new byte[40]));

        var act = () => this.parser.Parse(bytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*Piece count does not match total file size.*");
    }

    [Test]
    public void Parse_WhenPureBitTorrentV2WithFileTree_ParsesSuccessfully()
    {
        var fileMeta1 = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(50000) },
            { "pieces root", new BEncodedString(new byte[32]) },
        };
        var fileEntry1 = new BEncodedDictionary
        {
            { string.Empty, fileMeta1 },
        };

        var fileMeta2 = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(100000) },
            { "pieces root", new BEncodedString(new byte[32]) },
        };
        var fileEntry2 = new BEncodedDictionary
        {
            { string.Empty, fileMeta2 },
        };

        var subDirDict = new BEncodedDictionary
        {
            { "video.mp4", fileEntry2 },
        };

        var fileTree = new BEncodedDictionary
        {
            { "readme.txt", fileEntry1 },
            { "Season 1", subDirDict },
        };

        var infoDict = new BEncodedDictionary
        {
            { "meta version", new BEncodedNumber(2) },
            { "name", new BEncodedString("V2Torrent") },
            { "piece length", new BEncodedNumber(16384) },
            { "file tree", fileTree },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
        };

        var parsed = this.parser.Parse(rootDict.Encode());

        parsed.Name.Should().Be("V2Torrent");
        parsed.V1InfoHash.Should().BeNull();
        parsed.V2InfoHash.Should().NotBeNullOrEmpty();
        parsed.V2InfoHash.Length.Should().Be(64);
        parsed.InfoHash.Should().Be(parsed.V2InfoHash);
        parsed.Files.Should().HaveCount(2);
        parsed.Files.Should().Contain(f => f.Path == "readme.txt" && f.Size == 50000);
        parsed.Files.Should().Contain(f => f.Path == "Season 1/video.mp4" && f.Size == 100000);
        parsed.TotalSize.Should().Be(150000);
        parsed.PieceLength.Should().Be(16384);
        parsed.PieceCount.Should().Be((int)Math.Ceiling(150000.0 / 16384));
    }

    [Test]
    public void Parse_WhenHybridTorrent_SetsBothHashesAndParsesSuccessfully()
    {
        var fileMeta = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(16384) },
            { "pieces root", new BEncodedString(new byte[32]) },
        };
        var fileEntry = new BEncodedDictionary
        {
            { string.Empty, fileMeta },
        };
        var fileTree = new BEncodedDictionary
        {
            { "sample.iso", fileEntry },
        };

        var pieces = new byte[20];
        var infoDict = new BEncodedDictionary
        {
            { "meta version", new BEncodedNumber(2) },
            { "name", new BEncodedString("HybridTorrent") },
            { "piece length", new BEncodedNumber(16384) },
            { "pieces", new BEncodedString(pieces) },
            { "file tree", fileTree },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
        };

        var parsed = this.parser.Parse(rootDict.Encode());

        parsed.Name.Should().Be("HybridTorrent");
        parsed.V1InfoHash.Should().NotBeNullOrEmpty();
        parsed.V1InfoHash.Length.Should().Be(40);
        parsed.V2InfoHash.Should().NotBeNullOrEmpty();
        parsed.V2InfoHash.Length.Should().Be(64);
        parsed.InfoHash.Should().Be(parsed.V1InfoHash);
        parsed.Files.Should().HaveCount(1);
        parsed.Files[0].Path.Should().Be("sample.iso");
        parsed.TotalSize.Should().Be(16384);
    }

    [Test]
    public void Parse_WhenPaddingFilesPresentInV1_FiltersPaddingFiles()
    {
        var pieceLength = 16384;
        var pieces = new byte[20]; // 1 piece (16384 bytes)

        var regularFile = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(10000) },
            { "path", new BEncodedList { new BEncodedString("movie.mkv") } },
        };

        var padFileWithAttr = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(3000) },
            { "attr", new BEncodedString("p") },
            { "path", new BEncodedList { new BEncodedString("padding1.dat") } },
        };

        var padFileWithPath = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(3384) },
            { "path", new BEncodedList { new BEncodedString(".pad"), new BEncodedString("3384") } },
        };

        var filesList = new BEncodedList { regularFile, padFileWithAttr, padFileWithPath };

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString("PaddingTorrent") },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "files", filesList },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
        };

        var parsed = this.parser.Parse(rootDict.Encode());

        parsed.Files.Should().HaveCount(1);
        parsed.Files[0].Path.Should().Be("movie.mkv");
        parsed.Files[0].Size.Should().Be(10000);
        parsed.TotalSize.Should().Be(10000);
    }

    [Test]
    public void Parse_WhenPaddingFilesPresentInV2_FiltersPaddingFiles()
    {
        var regularMeta = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(5000) },
            { "pieces root", new BEncodedString(new byte[32]) },
        };
        var regularEntry = new BEncodedDictionary
        {
            { string.Empty, regularMeta },
        };

        var padMetaAttr = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(2000) },
            { "attr", new BEncodedString("p") },
        };
        var padEntryAttr = new BEncodedDictionary
        {
            { string.Empty, padMetaAttr },
        };

        var padMetaPath = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(3000) },
        };
        var padEntryPath = new BEncodedDictionary
        {
            { string.Empty, padMetaPath },
        };
        var padFolder = new BEncodedDictionary
        {
            { "3000", padEntryPath },
        };

        var fileTree = new BEncodedDictionary
        {
            { "file.txt", regularEntry },
            { "padfile.dat", padEntryAttr },
            { ".pad", padFolder },
        };

        var infoDict = new BEncodedDictionary
        {
            { "meta version", new BEncodedNumber(2) },
            { "name", new BEncodedString("V2PaddingTorrent") },
            { "piece length", new BEncodedNumber(16384) },
            { "file tree", fileTree },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
        };

        var parsed = this.parser.Parse(rootDict.Encode());

        parsed.Files.Should().HaveCount(1);
        parsed.Files[0].Path.Should().Be("file.txt");
        parsed.Files[0].Size.Should().Be(5000);
        parsed.TotalSize.Should().Be(5000);
    }

    [Test]
    public void Parse_WhenUtf8MetadataKeysPresent_PrioritizesUtf8Values()
    {
        var pieceLength = 16384;
        var pieces = new byte[20];

        var fileDict = new BEncodedDictionary
        {
            { "length", new BEncodedNumber(16384) },
            { "path", new BEncodedList { new BEncodedString("fallback_path.mkv") } },
            { "path.utf-8", new BEncodedList { new BEncodedString("utf8_path_🎬.mkv") } },
        };

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString("fallback_name") },
            { "name.utf-8", new BEncodedString("utf8_name_🚀") },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "files", new BEncodedList { fileDict } },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "comment", new BEncodedString("fallback_comment") },
            { "comment.utf-8", new BEncodedString("utf8_comment_💬") },
            { "info", infoDict },
        };

        var parsed = this.parser.Parse(rootDict.Encode());

        parsed.Name.Should().Be("utf8_name_🚀");
        parsed.Comment.Should().Be("utf8_comment_💬");
        parsed.Files.Should().HaveCount(1);
        parsed.Files[0].Path.Should().Be("utf8_path_🎬.mkv");
    }

    [Test]
    public void Parse_WhenBencodeExceedsMaxRecursionDepth_ThrowsInvalidTorrentFileException()
    {
        var sb = new StringBuilder();
        // 70 nested dictionaries
        for (var i = 0; i < 70; i++)
        {
            sb.Append("d1:k");
        }

        sb.Append("1:v");
        for (var i = 0; i < 70; i++)
        {
            sb.Append("e");
        }

        var deeplyNestedBytes = Encoding.UTF8.GetBytes(sb.ToString());

        var act = () => this.parser.Parse(deeplyNestedBytes);

        act.Should().Throw<InvalidTorrentFileException>()
            .WithMessage("*exceeds maximum recursion depth*");
    }
}
