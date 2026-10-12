// Copyright (c) FeedItOut. All rights reserved.

using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Subtitles;

namespace Leecharr.Core.Test.Subtitles;

[TestFixture]
public class SubtitleEncodingDetectorTest
{
    private SubtitleEncodingDetector detector = null!;

    [SetUp]
    public void SetUp()
    {
        this.detector = new SubtitleEncodingDetector();
    }

    [Test]
    public void DecodeToUtf8_Utf16LeWithoutBom_DecodesCyrillic()
    {
        const string srt = "1\r\n00:00:01,000 --> 00:00:04,000\r\nПривет\r\n";
        var bytes = Encoding.Unicode.GetBytes(srt);

        this.detector.DetectEncoding(bytes).Should().Be(Encoding.Unicode);
        this.detector.DecodeToUtf8(bytes).Should().Be(srt);
    }

    [Test]
    public void DecodeToUtf8_Utf16LeWithoutBom_DecodesLatinExtended()
    {
        const string srt = "1\r\n00:00:01,000 --> 00:00:04,000\r\ncafé\r\n";
        var bytes = Encoding.Unicode.GetBytes(srt);

        this.detector.DetectEncoding(bytes).Should().Be(Encoding.Unicode);
        this.detector.DecodeToUtf8(bytes).Should().Be(srt);
    }

    [Test]
    public void DecodeToUtf8_Utf16BeWithoutBom_DecodesCyrillic()
    {
        const string srt = "1\r\n00:00:01,000 --> 00:00:04,000\r\nПривет\r\n";
        var bytes = Encoding.BigEndianUnicode.GetBytes(srt);

        this.detector.DetectEncoding(bytes).Should().Be(Encoding.BigEndianUnicode);
        this.detector.DecodeToUtf8(bytes).Should().Be(srt);
    }

    [Test]
    public void DecodeToUtf8_Utf8WithoutBom_StillPreferredOverUtf16Heuristic()
    {
        const string srt = "1\n00:00:01,000 --> 00:00:04,000\nПривет\n";
        var bytes = Encoding.UTF8.GetBytes(srt);

        this.detector.DetectEncoding(bytes).CodePage.Should().Be(Encoding.UTF8.CodePage);
        this.detector.DecodeToUtf8(bytes).Should().Be(srt);
    }

    [Test]
    public void DecodeToUtf8_Utf16LeWithBom_StillDetected()
    {
        const string text = "Hello";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        this.detector.DecodeToUtf8(bytes).Should().Be(text);
    }
}
