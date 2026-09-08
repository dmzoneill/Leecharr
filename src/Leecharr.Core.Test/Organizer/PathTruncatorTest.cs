// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;

namespace Leecharr.Core.Test.Organizer;

[TestFixture]
public class PathTruncatorTest
{
    private PathTruncator truncator = null!;

    [SetUp]
    public void SetUp()
    {
        this.truncator = new PathTruncator();
    }

    [Test]
    public void TruncateFileName_WhenWithinByteLimit_ReturnsUnchanged()
    {
        var fileName = "A Great Show - S01E01 - Pilot [1080p].mkv";
        var result = this.truncator.TruncateFileName(fileName, 255);
        result.Should().Be(fileName);
    }

    [Test]
    public void TruncateFileName_WhenExceedsMaxBytes_PreservesExtensionAndTruncates()
    {
        var longTitle = new string('a', 300);
        var fileName = $"{longTitle}.mkv";

        var result = this.truncator.TruncateFileName(fileName, 255);

        Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(255);
        result.Should().EndWith(".mkv");
    }

    [Test]
    public void TruncateFileName_StructuredEpisode_PreservesPrefixAndSuffixWhileShorteningTitle()
    {
        var prefix = "Super Long Show Name - S01E01 - ";
        var longEpisodeTitle = new string('b', 240);
        var suffix = " [WEBDL-1080p x264 DTS-HD-FLUX]";
        var ext = ".mkv";

        var original = $"{prefix}{longEpisodeTitle}{suffix}{ext}";
        var result = this.truncator.TruncateFileName(original, 100);

        Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(100);
        result.Should().StartWith("Super Long Show Name - S01E01 - ");
        result.Should().EndWith("[WEBDL-1080p x264 DTS-HD-FLUX].mkv");
    }

    [Test]
    public void TruncateFileName_MultiByteCharacters_DoesNotSplitUtf8Bytes()
    {
        // 3-byte UTF-8 Japanese characters: こんにちは (15 bytes)
        var japaneseChars = string.Concat(Enumerable.Repeat("こんにちは", 20)); // 300 bytes
        var fileName = $"{japaneseChars}.mp4";

        var result = this.truncator.TruncateFileName(fileName, 50);

        Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(50);
        result.Should().EndWith(".mp4");
        // Verify valid UTF-8 string round-trip
        var bytes = Encoding.UTF8.GetBytes(result);
        var decoded = Encoding.UTF8.GetString(bytes);
        decoded.Should().Be(result);
    }

    [Test]
    public void TruncateFolderName_ExceedingBytes_TruncatesCleanly()
    {
        var longFolder = new string('f', 300);
        var result = this.truncator.TruncateFolderName(longFolder, 100);

        Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(100);
    }

    [Test]
    public void TruncatePath_WhenPathExceedsMaxChars_TruncatesFileNamePreservingStructure()
    {
        var path = "/media/tv/Long Series Name/" + new string('e', 200) + ".mkv";
        var result = this.truncator.TruncatePath(path, maxPathChars: 80, maxComponentBytes: 255);

        result.Length.Should().BeLessThanOrEqualTo(80);
        result.Should().StartWith("/media/tv/Long Series Name/");
        result.Should().EndWith(".mkv");
    }
}
