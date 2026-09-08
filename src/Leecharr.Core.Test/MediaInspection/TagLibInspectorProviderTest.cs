// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaInspection;

namespace Leecharr.Core.Test.MediaInspection;

[TestFixture]
public class TagLibInspectorProviderTest
{
    private TagLibInspectorProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        this.provider = new TagLibInspectorProvider();
    }

    [Test]
    public void Inspect_WithUnseekableStream_HandlesCleanlyWithoutThrowing()
    {
        var ebmlData = CreateMatroskaHeader("matroska", "V_MPEGH/ISO/HEVC", 3840, 2160, "A_EAC3", 6);
        using var unseekable = new UnseekableStream(ebmlData);

        unseekable.CanSeek.Should().BeFalse();

        var result = this.provider.Inspect(unseekable, "test.mkv");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("Matroska (MKV)");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.Width.Should().Be(3840);
        result.Height.Should().Be(2160);
        result.AudioCodec.Should().Be("E-AC3 / Dolby Digital Plus");
        result.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void Inspect_BinaryEbmlParser_IdentifiesMatroskaHeader()
    {
        var ebmlData = CreateMatroskaHeader("matroska", "V_MPEGH/ISO/HEVC", 1920, 1080, "A_TRUEHD", 8);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "movie.mkv");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("Matroska (MKV)");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
        result.AudioCodec.Should().Be("Dolby TrueHD / Atmos");
        result.AudioChannels.Should().Be("7.1");
    }

    [Test]
    public void Inspect_BinaryEbmlParser_IdentifiesWebMHeader()
    {
        var ebmlData = CreateMatroskaHeader("webm", "V_VP9", 1280, 720, "A_OPUS", 2);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "clip.webm");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("WebM");
        result.VideoCodec.Should().Be("VP9");
        result.Width.Should().Be(1280);
        result.Height.Should().Be(720);
        result.AudioCodec.Should().Be("Opus");
        result.AudioChannels.Should().Be("2.0");
    }

    [TestCase("A_AAC", 6, "AAC", "5.1")]
    [TestCase("A_AAC", 8, "AAC", "7.1")]
    [TestCase("A_AAC", 1, "AAC", "1.0")]
    [TestCase("A_OPUS", 6, "Opus", "5.1")]
    [TestCase("A_OPUS", 8, "Opus", "7.1")]
    [TestCase("A_FLAC", 6, "FLAC", "5.1")]
    [TestCase("A_AC3", 2, "AC3 / Dolby Digital", "2.0")]
    [TestCase("A_EAC3", 2, "E-AC3 / Dolby Digital Plus", "2.0")]
    [TestCase("A_EAC3/JOC", 6, "Dolby Atmos", "5.1")]
    [TestCase("A_EAC3/JOC", 8, "Dolby Atmos", "7.1")]
    [TestCase("A_TRUEHD", 6, "Dolby TrueHD / Atmos", "5.1")]
    public void Inspect_Matroska_EbmlChannelsOverridesCodecIdDefaults(string audioCodecId, int channelCount, string expectedCodec, string expectedChannels)
    {
        var ebmlData = CreateMatroskaHeader("matroska", "V_MPEGH/ISO/HEVC", 1920, 1080, audioCodecId, channelCount);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "movie.mkv");

        result.Should().NotBeNull();
        result.AudioCodec.Should().Be(expectedCodec);
        result.AudioChannels.Should().Be(expectedChannels);
    }

    [Test]
    public void Inspect_NonFaststartMp4WithMdatContainingAv01_DoesNotFalselyDetectAv1AndFindsMoov()
    {
        // 1. ftyp box
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));

        // 2. mdat box with high entropy payload containing "av01" substring bytes
        var mdatPayload = new byte[70000];
        Array.Fill(mdatPayload, (byte)0xCC);
        var av01Bytes = Encoding.ASCII.GetBytes("av01");
        Array.Copy(av01Bytes, 0, mdatPayload, 1000, av01Bytes.Length);
        var mdat = CreateMp4Box("mdat", mdatPayload);

        // 3. moov box at EOF with H.264 (avc1) and AAC (mp4a)
        var videoEntry = CreateVisualSampleEntry("avc1", 1920, 1080);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("mp4a", 2, 16, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(mdat, 0, mdat.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "movie.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("H.264");
        result.VideoCodec.Should().NotBe("AV1");
        result.AudioCodec.Should().Be("AAC");
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
        result.Resolution.Should().Be("1080p");
        result.AudioChannels.Should().Be("2.0");
        result.AudioSampleRate.Should().Be(48000);
    }

    [Test]
    public void Inspect_Mp4WithLargeSizeBox_CorrectlyParsedAndDetectsHevcDolbyVision()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));
        var mdatPayload = new byte[5000];
        var mdatLarge = CreateMp4LargeBox("mdat", mdatPayload);

        var videoEntry = CreateVisualSampleEntry("hvc1", 3840, 2160, "dvcC");
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("ec-3", 6, 16, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(mdatLarge, 0, mdatLarge.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "video.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.HdrFormat.Should().Be("Dolby Vision");
        result.AudioCodec.Should().Be("E-AC3 / Dolby Digital Plus");
        result.Width.Should().Be(3840);
        result.Height.Should().Be(2160);
        result.Resolution.Should().Be("4K UHD (2160p)");
        result.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void Inspect_Mp4WithMoovAtBeginning_IdentifiesAV1()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));

        var videoEntry = CreateVisualSampleEntry("av01", 1280, 720);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("Opus", 2, 16, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "clip.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("AV1");
        result.AudioCodec.Should().Be("Opus");
        result.Width.Should().Be(1280);
        result.Height.Should().Be(720);
        result.Resolution.Should().Be("720p");
    }

    [Test]
    public void Inspect_Mp4StartingWithMoovBox_IdentifiesCorrectly()
    {
        var videoEntry = CreateVisualSampleEntry("vp09", 1920, 1080);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));
        var moov = CreateMoovBox(videoTrak);

        using var ms = new MemoryStream();
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "stream.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("VP9");
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
    }

    [Test]
    public void Inspect_Mp4WithAac5Point1_DetectsAacAnd5Point1Channels()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));
        var videoEntry = CreateVisualSampleEntry("avc1", 1920, 1080);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("mp4a", 6, 16, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "video_51_aac.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("H.264");
        result.AudioCodec.Should().Be("AAC");
        result.AudioChannels.Should().Be("5.1");
        result.AudioSampleRate.Should().Be(48000);
    }

    [Test]
    public void Inspect_Mp4WithAac7Point1_DetectsAacAnd7Point1Channels()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));
        var videoEntry = CreateVisualSampleEntry("avc1", 1920, 1080);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("mp4a", 8, 16, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "video_71_aac.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("H.264");
        result.AudioCodec.Should().Be("AAC");
        result.AudioChannels.Should().Be("7.1");
    }

    [Test]
    public void Inspect_Mp4WithAc3Stereo_DetectsAc3And2Point0Channels()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));
        var videoEntry = CreateVisualSampleEntry("hvc1", 1920, 1080);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("ac-3", 2, 16, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "video_stereo_ac3.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.AudioCodec.Should().Be("AC3 / Dolby Digital");
        result.AudioChannels.Should().Be("2.0");
    }

    [Test]
    public void Inspect_Mp4WithEac3Stereo_DetectsEac3And2Point0Channels()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));
        var videoEntry = CreateVisualSampleEntry("hvc1", 1920, 1080);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("ec-3", 2, 16, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "video_stereo_eac3.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.AudioCodec.Should().Be("E-AC3 / Dolby Digital Plus");
        result.AudioChannels.Should().Be("2.0");
    }

    [Test]
    public void Inspect_Mp4WithFlacSurround_DetectsFlacAnd5Point1Channels()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));
        var videoEntry = CreateVisualSampleEntry("av01", 1920, 1080);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry = CreateAudioSampleEntry("flac", 6, 24, 48000);
        var audioTrak = CreateTrackBox(CreateStsdBox(audioEntry));

        var moov = CreateMoovBox(videoTrak, audioTrak);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "video_flac_51.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.AudioCodec.Should().Be("FLAC");
        result.AudioChannels.Should().Be("5.1");
        result.AudioBitDepth.Should().Be(24);
        result.AudioSampleRate.Should().Be(48000);
    }

    [Test]
    public void Inspect_MultiTrackMkv_PreservesPrimaryTrueHdAndPopulatesSubtitleTracks()
    {
        var audioTracks = new (string, int)[]
        {
            ("A_TRUEHD", 8),
            ("A_AC3", 6),
            ("A_AAC", 2),
        };
        var subTracks = new string[]
        {
            "S_TEXT/UTF8",
            "S_TEXT/ASS",
            "S_HDMV/PGS",
        };

        var ebmlData = CreateMultiTrackMatroskaHeader("matroska", "V_MPEGH/ISO/HEVC", 3840, 2160, audioTracks, subTracks);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "movie.mkv");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("Matroska (MKV)");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.Width.Should().Be(3840);
        result.Height.Should().Be(2160);
        result.AudioCodec.Should().Be("Dolby TrueHD / Atmos");
        result.AudioChannels.Should().Be("7.1");
        result.SubtitleTracks.Should().ContainInOrder("SubRip (SRT)", "Advanced SubStation Alpha", "PGS Subtitles");
    }

    [Test]
    public void Inspect_MultiTrackMkv_UpgradesAudioCodecWhenHigherFidelityFollows()
    {
        var audioTracks = new (string, int)[]
        {
            ("A_AC3", 6),
            ("A_TRUEHD", 8),
        };
        var subTracks = new string[]
        {
            "S_VOBSUB",
            "S_DVBSUB",
        };

        var ebmlData = CreateMultiTrackMatroskaHeader("matroska", "V_MPEG4/ISO/AVC", 1920, 1080, audioTracks, subTracks);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "show.mkv");

        result.Should().NotBeNull();
        result.AudioCodec.Should().Be("Dolby TrueHD / Atmos");
        result.AudioChannels.Should().Be("7.1");
        result.SubtitleTracks.Should().ContainInOrder("VobSub", "DVB Subtitles");
    }

    [Test]
    public void Inspect_MultiTrackMkv_DeduplicatesSubtitleTracks()
    {
        var audioTracks = new (string, int)[]
        {
            ("A_DTS", 6),
        };
        var subTracks = new string[]
        {
            "S_TEXT/UTF8",
            "S_TEXT/UTF8",
            "S_TEXT/SSA",
        };

        var ebmlData = CreateMultiTrackMatroskaHeader("matroska", "V_MPEG4/ISO/AVC", 1920, 1080, audioTracks, subTracks);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "show.mkv");

        result.Should().NotBeNull();
        result.SubtitleTracks.Should().HaveCount(2);
        result.SubtitleTracks.Should().ContainInOrder("SubRip (SRT)", "SubStation Alpha");
    }

    [Test]
    public void Inspect_Mp4WithMultipleAudioAndSubtitleTracks_PreservesPrimaryAudioAndPopulatesSubtitleTracks()
    {
        var ftyp = CreateMp4Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0isommp41"));

        var videoEntry = CreateVisualSampleEntry("hvc1", 3840, 2160);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));

        var audioEntry1 = CreateAudioSampleEntry("mlpa", 8, 24, 48000);
        var audioTrak1 = CreateTrackBox(CreateStsdBox(audioEntry1));

        var audioEntry2 = CreateAudioSampleEntry("mp4a", 2, 16, 48000);
        var audioTrak2 = CreateTrackBox(CreateStsdBox(audioEntry2));

        var subEntry1 = CreateMp4Box("tx3g", new byte[10]);
        var subTrak1 = CreateTrackBox(CreateStsdBox(subEntry1));

        var subEntry2 = CreateMp4Box("wvtt", new byte[10]);
        var subTrak2 = CreateTrackBox(CreateStsdBox(subEntry2));

        var moov = CreateMoovBox(videoTrak, audioTrak1, audioTrak2, subTrak1, subTrak2);

        using var ms = new MemoryStream();
        ms.Write(ftyp, 0, ftyp.Length);
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "movie.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.AudioCodec.Should().Be("Dolby TrueHD / Atmos");
        result.AudioChannels.Should().Be("7.1");
        result.SubtitleTracks.Should().Contain("tx3g");
        result.SubtitleTracks.Should().Contain("WebVTT");
    }

    [Test]
    public void Inspect_Matroska_WithHdr10ColourElement_DetectsHdr10()
    {
        var ebmlData = CreateMatroskaHeaderWithColour("matroska", "V_MPEGH/ISO/HEVC", 3840, 2160, 16, 9);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "sample.mkv");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("Matroska (MKV)");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.HdrFormat.Should().Be("HDR10");
        result.Resolution.Should().Be("4K UHD (2160p)");
    }

    [Test]
    public void Inspect_Matroska_WithHlgColourElement_DetectsHlg()
    {
        var ebmlData = CreateMatroskaHeaderWithColour("matroska", "V_MPEGH/ISO/HEVC", 3840, 2160, 18, 9);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "sample.mkv");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("Matroska (MKV)");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.HdrFormat.Should().Be("HLG");
    }

    [Test]
    public void Inspect_Mp4_WithColrBox_DetectsHdr10()
    {
        var colrBox = CreateColrBox(9, 16, 9);
        var videoEntry = CreateVisualSampleEntryWithExtraBox("hvc1", 3840, 2160, colrBox);
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));
        var moov = CreateMoovBox(videoTrak);

        using var ms = new MemoryStream();
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "video.mp4");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP4");
        result.VideoCodec.Should().Be("HEVC (H.265)");
        result.HdrFormat.Should().Be("HDR10");
    }

    [Test]
    public void Inspect_Mp4_WithDolbyVisionAndColrBox_DetectsHybridDolbyVisionAndHdr10()
    {
        var colrBox = CreateColrBox(9, 16, 9);
        var dvcCBox = CreateMp4Box("dvcC", new byte[8]);
        using var extraBoxes = new MemoryStream();
        extraBoxes.Write(dvcCBox, 0, dvcCBox.Length);
        extraBoxes.Write(colrBox, 0, colrBox.Length);

        var videoEntry = CreateVisualSampleEntryWithExtraBox("hvc1", 3840, 2160, extraBoxes.ToArray());
        var videoTrak = CreateTrackBox(CreateStsdBox(videoEntry));
        var moov = CreateMoovBox(videoTrak);

        using var ms = new MemoryStream();
        ms.Write(moov, 0, moov.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "hybrid_hdr.mp4");

        result.Should().NotBeNull();
        result.HdrFormat.Should().Be("Dolby Vision / HDR10");
    }

    [TestCase(720, 576, "576p")]
    [TestCase(1024, 576, "576p")]
    [TestCase(1920, 800, "1080p")]
    [TestCase(3840, 1600, "4K UHD (2160p)")]
    public void Inspect_Matroska_Non16By9AndPal576p_RespectsTrackDimensionsAndResolution(int width, int height, string expectedResolution)
    {
        var ebmlData = CreateMatroskaHeader("matroska", "V_MPEGH/ISO/HEVC", width, height, "A_EAC3", 6);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "movie.mkv");

        result.Should().NotBeNull();
        result.Width.Should().Be(width);
        result.Height.Should().Be(height);
        result.Resolution.Should().Be(expectedResolution);
    }

    [Test]
    public void Inspect_Matroska_WithTrueHd6Channels_DoesNotInflateTo7Point1()
    {
        var ebmlData = CreateMatroskaHeader("matroska", "V_MPEGH/ISO/HEVC", 1920, 1080, "A_TRUEHD", 6);
        using var ms = new MemoryStream(ebmlData);

        var result = this.provider.Inspect(ms, "movie.mkv");

        result.Should().NotBeNull();
        result.AudioCodec.Should().Be("Dolby TrueHD / Atmos");
        result.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void ApplyFilenameHints_DoesNotOverwriteVerifiedDimensionsCodecsOrChannels()
    {
        var info = new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
            Width = 1920,
            Height = 800,
            Resolution = "1080p",
            VideoCodec = "HEVC (H.265)",
            AudioCodec = "AAC",
            AudioChannels = "2.0",
            HdrFormat = "HDR10",
        };

        TagLibInspectorProvider.ApplyFilenameHints(info, "Movie.Title.2023.1080p.WEBRip.x264.DTS.DV.mkv");

        info.Width.Should().Be(1920);
        info.Height.Should().Be(800);
        info.Resolution.Should().Be("1080p");
        info.VideoCodec.Should().Be("HEVC (H.265)");
        info.AudioCodec.Should().Be("AAC");
        info.AudioChannels.Should().Be("2.0");
        info.HdrFormat.Should().Be("HDR10");
    }

    [Test]
    public void ApplyFilenameHints_AvoidsSubstringFalsePositivesOnOrdinaryTitles()
    {
        var info = new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
        };

        TagLibInspectorProvider.ApplyFilenameHints(info, "The.Adventures.of.Tintin.DVDRip.x264.mkv");

        info.HdrFormat.Should().BeNull();
        info.VideoCodec.Should().Be("AVC / H.264");

        var sdhInfo = new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
            Resolution = "1080p",
            Width = 1920,
            Height = 1080,
        };

        TagLibInspectorProvider.ApplyFilenameHints(sdhInfo, "Show.S01E01.1080p.WEBRip.x265.SDH.mkv");

        sdhInfo.Resolution.Should().Be("1080p");
        sdhInfo.Width.Should().Be(1920);
        sdhInfo.Height.Should().Be(1080);
    }

    [Test]
    public void Inspect_FlacStream_ExtractsStreamInfoAndCalculatesDuration()
    {
        // 44.1kHz, stereo (2ch), 16-bit, 882,000 samples = 20.0 seconds
        var flacData = CreateFlacHeader(44100, 2, 16, 882000L);
        using var ms = new MemoryStream(flacData);

        var result = this.provider.Inspect(ms, "track.flac");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("FLAC");
        result.AudioCodec.Should().Be("FLAC");
        result.AudioSampleRate.Should().Be(44100);
        result.AudioChannels.Should().Be("2.0");
        result.AudioBitDepth.Should().Be(16);
        result.DurationSeconds.Should().BeApproximately(20.0, 0.001);
    }

    [Test]
    public void Inspect_FlacStream_MultiChannelHiRes_ExtractsMetadataAccurately()
    {
        // 96kHz, 5.1 surround (6ch), 24-bit, 4,800,000 samples = 50.0 seconds
        var flacData = CreateFlacHeader(96000, 6, 24, 4800000L);
        using var ms = new MemoryStream(flacData);

        var result = this.provider.Inspect(ms, "album.flac");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("FLAC");
        result.AudioCodec.Should().Be("FLAC");
        result.AudioSampleRate.Should().Be(96000);
        result.AudioChannels.Should().Be("5.1");
        result.AudioBitDepth.Should().Be(24);
        result.DurationSeconds.Should().BeApproximately(50.0, 0.001);
    }

    [Test]
    public void Inspect_FlacStream_WithHeaderShorterThan26_ExtractsAudioPropertiesWithoutDuration()
    {
        var flacData = CreateFlacHeader(48000, 8, 24, 0);
        var truncated = new byte[23];
        Array.Copy(flacData, 0, truncated, 0, 23);

        using var ms = new MemoryStream(truncated);
        var result = this.provider.Inspect(ms, "truncated.flac");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("FLAC");
        result.AudioCodec.Should().Be("FLAC");
        result.AudioSampleRate.Should().Be(48000);
        result.AudioChannels.Should().Be("7.1");
        result.AudioBitDepth.Should().Be(24);
        result.DurationSeconds.Should().Be(0.0);
    }

    [Test]
    public void Inspect_FlacStream_WithHeaderShorterThan22_LeavesAudioPropertiesUnpopulated()
    {
        var truncated = new byte[15];
        truncated[0] = (byte)'f';
        truncated[1] = (byte)'L';
        truncated[2] = (byte)'a';
        truncated[3] = (byte)'C';

        using var ms = new MemoryStream(truncated);
        var result = this.provider.Inspect(ms, "headeronly.flac");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("FLAC");
        result.AudioCodec.Should().Be("FLAC");
        result.AudioSampleRate.Should().Be(0);
        result.AudioChannels.Should().BeNull();
        result.AudioBitDepth.Should().Be(0);
        result.DurationSeconds.Should().Be(0.0);
    }

    [Test]
    public void Inspect_FlacWithId3Tag_ParsesTrailingFlacStreamInfoAndDuration()
    {
        var flacData = CreateFlacHeader(44100, 2, 16, 441000L); // 10.0 seconds
        var id3Tag = new byte[20];
        id3Tag[0] = (byte)'I';
        id3Tag[1] = (byte)'D';
        id3Tag[2] = (byte)'3';
        id3Tag[3] = 0x03; // ID3v2.3
        id3Tag[4] = 0x00;
        id3Tag[5] = 0x00;
        // Size: 10 bytes payload -> tagOffset = 10 + 10 = 20
        id3Tag[6] = 0x00;
        id3Tag[7] = 0x00;
        id3Tag[8] = 0x00;
        id3Tag[9] = 10;

        using var ms = new MemoryStream();
        ms.Write(id3Tag, 0, id3Tag.Length);
        ms.Write(flacData, 0, flacData.Length);
        ms.Position = 0;

        var result = this.provider.Inspect(ms, "tagged.flac");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("FLAC");
        result.AudioCodec.Should().Be("FLAC");
        result.AudioSampleRate.Should().Be(44100);
        result.AudioChannels.Should().Be("2.0");
        result.AudioBitDepth.Should().Be(16);
        result.DurationSeconds.Should().BeApproximately(10.0, 0.001);
    }

    private static byte[] CreateFlacHeader(int sampleRate, int channels, int bitDepth, ulong totalSamples)
    {
        var data = new byte[32];

        // 'fLaC' magic
        data[0] = (byte)'f';
        data[1] = (byte)'L';
        data[2] = (byte)'a';
        data[3] = (byte)'C';
        data[4] = 0x00; // METADATA_BLOCK_HEADER
        data[5] = 0x00;
        data[6] = 0x00;
        data[7] = 0x22; // 34 bytes

        var chMinus1 = channels - 1;
        var bpsMinus1 = bitDepth - 1;

        data[18] = (byte)((sampleRate >> 12) & 0xFF);
        data[19] = (byte)((sampleRate >> 4) & 0xFF);
        data[20] = (byte)(((sampleRate & 0x0F) << 4) | ((chMinus1 & 0x07) << 1) | ((bpsMinus1 >> 4) & 0x01));
        data[21] = (byte)(((bpsMinus1 & 0x0F) << 4) | (int)((totalSamples >> 32) & 0x0F));
        data[22] = (byte)((totalSamples >> 24) & 0xFF);
        data[23] = (byte)((totalSamples >> 16) & 0xFF);
        data[24] = (byte)((totalSamples >> 8) & 0xFF);
        data[25] = (byte)(totalSamples & 0xFF);

        return data;
    }

    [Test]
    public void Inspect_StandardPcmWav_IdentifiesWavCodecChannelsSampleRateAndBitDepth()
    {
        var wavData = CreateWavHeader(formatTag: 1, channels: 2, sampleRate: 44100, bitsPerSample: 16);
        using var ms = new MemoryStream(wavData);

        var result = this.provider.Inspect(ms, string.Empty);

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("WAV");
        result.AudioCodec.Should().Be("PCM");
        result.AudioChannels.Should().Be("2.0");
        result.AudioSampleRate.Should().Be(44100);
        result.AudioBitDepth.Should().Be(16);
    }

    [TestCase((ushort)1, 22050, (ushort)8, "1.0", 22050, 8)]
    [TestCase((ushort)6, 48000, (ushort)24, "5.1", 48000, 24)]
    [TestCase((ushort)8, 96000, (ushort)32, "7.1", 96000, 32)]
    public void Inspect_MultiChannelWav_IdentifiesChannelsSampleRateAndBitDepth(
        ushort channels,
        int sampleRate,
        ushort bitsPerSample,
        string expectedChannels,
        int expectedSampleRate,
        int expectedBitDepth)
    {
        var wavData = CreateWavHeader(formatTag: 1, channels: channels, sampleRate: (uint)sampleRate, bitsPerSample: bitsPerSample);
        using var ms = new MemoryStream(wavData);

        var result = this.provider.Inspect(ms, "stream_sample.wav");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("WAV");
        result.AudioCodec.Should().Be("PCM");
        result.AudioChannels.Should().Be(expectedChannels);
        result.AudioSampleRate.Should().Be(expectedSampleRate);
        result.AudioBitDepth.Should().Be(expectedBitDepth);
    }

    [TestCase((ushort)1, "PCM")]
    [TestCase((ushort)3, "IEEE Float")]
    [TestCase((ushort)6, "ALaw")]
    [TestCase((ushort)7, "MuLaw")]
    [TestCase((ushort)0x0055, "MP3")]
    [TestCase((ushort)0x00FF, "AAC")]
    [TestCase((ushort)0x2000, "AC3")]
    [TestCase((ushort)0x2001, "DTS")]
    [TestCase((ushort)0xFFFE, "PCM")]
    public void Inspect_Wav_WithVariousFormatTags_CorrectlyMapsCodec(ushort formatTag, string expectedCodec)
    {
        var wavData = CreateWavHeader(formatTag: formatTag, channels: 2, sampleRate: 48000, bitsPerSample: 24);
        using var ms = new MemoryStream(wavData);

        var result = this.provider.Inspect(ms, string.Empty);

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("WAV");
        result.AudioCodec.Should().Be(expectedCodec);
        result.AudioChannels.Should().Be("2.0");
        result.AudioSampleRate.Should().Be(48000);
        result.AudioBitDepth.Should().Be(24);
    }

    [Test]
    public void Inspect_Wav_WithPrecedingJunkChunk_CorrectlyParsesFmtChunk()
    {
        var wavData = CreateWavHeader(formatTag: 1, channels: 6, sampleRate: 48000, bitsPerSample: 24, includeJunkChunk: true);
        using var ms = new MemoryStream(wavData);

        var result = this.provider.Inspect(ms, string.Empty);

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("WAV");
        result.AudioCodec.Should().Be("PCM");
        result.AudioChannels.Should().Be("5.1");
        result.AudioSampleRate.Should().Be(48000);
        result.AudioBitDepth.Should().Be(24);
    }

    [Test]
    public void Inspect_Id3TaggedWav_CorrectlyParsesWavProperties()
    {
        var wavData = CreateId3v2WavHeader(formatTag: 1, channels: 6, sampleRate: 48000, bitsPerSample: 24);
        using var ms = new MemoryStream(wavData);

        var result = this.provider.Inspect(ms, "recording.wav");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("WAV");
        result.AudioCodec.Should().Be("PCM");
        result.AudioChannels.Should().Be("5.1");
        result.AudioSampleRate.Should().Be(48000);
        result.AudioBitDepth.Should().Be(24);
    }

    [Test]
    public void Inspect_WavWithUnseekableStream_ParsesSuccessfully()
    {
        var wavData = CreateWavHeader(formatTag: 1, channels: 2, sampleRate: 44100, bitsPerSample: 16);
        using var unseekable = new UnseekableStream(wavData);

        unseekable.CanSeek.Should().BeFalse();

        var result = this.provider.Inspect(unseekable, string.Empty);

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("WAV");
        result.AudioCodec.Should().Be("PCM");
        result.AudioChannels.Should().Be("2.0");
        result.AudioSampleRate.Should().Be(44100);
        result.AudioBitDepth.Should().Be(16);
    }

    [Test]
    public void InspectByFileName_WithWavExtension_ReturnsWavContainerInfo()
    {
        var result = TagLibInspectorProvider.InspectByFileName("audio_sample.wav");

        result.Should().NotBeNull();
        result!.ContainerFormat.Should().Be("WAV");
        result.AudioCodec.Should().Be("PCM");
    }

    private static byte[] CreateWavHeader(
        ushort formatTag = 1,
        ushort channels = 2,
        uint sampleRate = 44100,
        ushort bitsPerSample = 16,
        bool includeJunkChunk = false)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0); // placeholder for RIFF size
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        if (includeJunkChunk)
        {
            writer.Write(Encoding.ASCII.GetBytes("JUNK"));
            writer.Write(4); // 4 bytes junk
            writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        }

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // subchunk size for standard PCM
        writer.Write(formatTag);
        writer.Write(channels);
        writer.Write(sampleRate);
        uint byteRate = sampleRate * channels * (uint)(bitsPerSample / 8);
        writer.Write(byteRate);
        ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        // data chunk header
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(0);

        var data = ms.ToArray();
        // Update RIFF chunk size at offset 4
        int riffSize = data.Length - 8;
        data[4] = (byte)(riffSize & 0xFF);
        data[5] = (byte)((riffSize >> 8) & 0xFF);
        data[6] = (byte)((riffSize >> 16) & 0xFF);
        data[7] = (byte)((riffSize >> 24) & 0xFF);

        return data;
    }

    private static byte[] CreateId3v2WavHeader(
        ushort formatTag = 1,
        ushort channels = 2,
        uint sampleRate = 44100,
        ushort bitsPerSample = 16)
    {
        var wav = CreateWavHeader(formatTag, channels, sampleRate, bitsPerSample);
        using var ms = new MemoryStream();

        // ID3v2 header (10 bytes): 'ID3', version 2.4, flags 0, tag size 10 (syncsafe)
        ms.Write(Encoding.ASCII.GetBytes("ID3"));
        ms.WriteByte(4);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x0A });

        // 10 bytes tag body
        ms.Write(new byte[10]);

        // WAV data
        ms.Write(wav, 0, wav.Length);

        return ms.ToArray();
    }

    [Test]
    public void Inspect_AviWithXvidAndAc3Surround_DetectsXvidAndAc3With5Point1Channels()
    {
        var aviData = CreateAviHeader("XVID", 1280, 720, audioFormatTag: 0x2000, audioChannels: 6, audioSampleRate: 48000);
        using var ms = new MemoryStream(aviData);

        var result = this.provider.Inspect(ms, "movie.avi");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("AVI");
        result.VideoCodec.Should().Be("Xvid / MPEG-4");
        result.Width.Should().Be(1280);
        result.Height.Should().Be(720);
        result.Resolution.Should().Be("720p");
        result.AudioCodec.Should().Be("AC3 / Dolby Digital");
        result.AudioChannels.Should().Be("5.1");
        result.AudioSampleRate.Should().Be(48000);
    }

    [Test]
    public void Inspect_AviWithH264AndPcmStereo_DetectsH264AndPcmWith2Point0Channels()
    {
        var aviData = CreateAviHeader("H264", 1920, 1080, audioFormatTag: 0x0001, audioChannels: 2, audioSampleRate: 44100, audioBitsPerSample: 16);
        using var ms = new MemoryStream(aviData);

        var result = this.provider.Inspect(ms, "clip.avi");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("AVI");
        result.VideoCodec.Should().Be("H.264");
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
        result.Resolution.Should().Be("1080p");
        result.AudioCodec.Should().Be("PCM");
        result.AudioChannels.Should().Be("2.0");
        result.AudioSampleRate.Should().Be(44100);
        result.AudioBitDepth.Should().Be(16);
    }

    [Test]
    public void Inspect_AviWithDivxAndMp3_DetectsXvidAndMp3()
    {
        var aviData = CreateAviHeader("DIVX", 640, 480, audioFormatTag: 0x0055, audioChannels: 2, audioSampleRate: 44100);
        using var ms = new MemoryStream(aviData);

        var result = this.provider.Inspect(ms, "episode.avi");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("AVI");
        result.VideoCodec.Should().Be("Xvid / MPEG-4");
        result.Width.Should().Be(640);
        result.Height.Should().Be(480);
        result.Resolution.Should().Be("480p");
        result.AudioCodec.Should().Be("MP3");
        result.AudioChannels.Should().Be("2.0");
        result.AudioSampleRate.Should().Be(44100);
    }

    [Test]
    public void Inspect_AviWithDtsSurround_DetectsDtsAnd5Point1Channels()
    {
        var aviData = CreateAviHeader("XVID", 1920, 1080, audioFormatTag: 0x2001, audioChannels: 6, audioSampleRate: 48000);
        using var ms = new MemoryStream(aviData);

        var result = this.provider.Inspect(ms, "film.avi");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("AVI");
        result.AudioCodec.Should().Be("DTS");
        result.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void Inspect_AviWithMonoAudio_Detects1Point0Channel()
    {
        var aviData = CreateAviHeader("XVID", 640, 480, audioFormatTag: 0x0001, audioChannels: 1, audioSampleRate: 22050);
        using var ms = new MemoryStream(aviData);

        var result = this.provider.Inspect(ms, "classic.avi");

        result.Should().NotBeNull();
        result.AudioCodec.Should().Be("PCM");
        result.AudioChannels.Should().Be("1.0");
    }

    [Test]
    public void Inspect_SilentAviWithoutAudioStream_LeavesAudioCodecAndChannelsNull()
    {
        var aviData = CreateAviHeader("XVID", 1280, 720, audioFormatTag: null);
        using var ms = new MemoryStream(aviData);

        var result = this.provider.Inspect(ms, "cctv_footage.avi");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("AVI");
        result.VideoCodec.Should().Be("Xvid / MPEG-4");
        result.Width.Should().Be(1280);
        result.Height.Should().Be(720);
        result.AudioCodec.Should().BeNull();
        result.AudioChannels.Should().BeNull();
    }

    [Test]
    public void Inspect_AviWithUnseekableStream_ParsesSuccessfully()
    {
        var aviData = CreateAviHeader("XVID", 1280, 720, audioFormatTag: 0x2000, audioChannels: 6);
        using var unseekable = new UnseekableStream(aviData);

        var result = this.provider.Inspect(unseekable, "stream.avi");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("AVI");
        result.VideoCodec.Should().Be("Xvid / MPEG-4");
        result.AudioCodec.Should().Be("AC3 / Dolby Digital");
        result.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void Inspect_Mp3Stream_DoesNotPopulateHardcodedChannelsSampleRateOrBitDepth()
    {
        var mp3Data = new byte[64];
        mp3Data[0] = 0xFF;
        mp3Data[1] = 0xFB;

        using var ms = new MemoryStream(mp3Data);
        var result = this.provider.Inspect(ms, "track.mp3");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP3");
        result.AudioCodec.Should().Be("MP3");
        result.AudioChannels.Should().BeNull();
        result.AudioSampleRate.Should().Be(0);
        result.AudioBitDepth.Should().Be(0);
    }

    [Test]
    public void Inspect_Mp3Stream_WithId3Tag_DoesNotPopulateHardcodedChannelsSampleRateOrBitDepth()
    {
        var id3Data = new byte[64];
        id3Data[0] = (byte)'I';
        id3Data[1] = (byte)'D';
        id3Data[2] = (byte)'3';

        using var ms = new MemoryStream(id3Data);
        var result = this.provider.Inspect(ms, "song.mp3");

        result.Should().NotBeNull();
        result.ContainerFormat.Should().Be("MP3");
        result.AudioCodec.Should().Be("MP3");
        result.AudioChannels.Should().BeNull();
        result.AudioSampleRate.Should().Be(0);
        result.AudioBitDepth.Should().Be(0);
    }

    [Test]
    public void InspectByFileName_WithMp3Extension_ReturnsMp3ContainerInfoWithoutHardcodedStreamProperties()
    {
        var result = TagLibInspectorProvider.InspectByFileName("podcast.mp3");

        result.Should().NotBeNull();
        result!.ContainerFormat.Should().Be("MP3");
        result.AudioCodec.Should().Be("MP3");
        result.AudioChannels.Should().BeNull();
        result.AudioSampleRate.Should().Be(0);
        result.AudioBitDepth.Should().Be(0);
    }

    [TestCase(48000, 1, "1.0", 48000)]
    [TestCase(44100, 1, "1.0", 44100)]
    [TestCase(32000, 1, "1.0", 32000)]
    [TestCase(48000, 2, "2.0", 48000)]
    [TestCase(44100, 2, "2.0", 44100)]
    [TestCase(32000, 2, "2.0", 32000)]
    public void InspectFile_Mp3_AccuratelyPopulatesTagLibProperties(int sampleRate, int channels, string expectedChannels, int expectedSampleRate)
    {
        var mp3Bytes = CreateMp3Data(sampleRate, channels, frameCount: 20);
        var tempFile = Path.Combine(Path.GetTempPath(), $"leecharr_test_{Guid.NewGuid():N}.mp3");

        try
        {
            File.WriteAllBytes(tempFile, mp3Bytes);

            var result = this.provider.InspectFile(tempFile);

            result.Should().NotBeNull();
            result.ContainerFormat.Should().Be("MP3");
            result.AudioChannels.Should().Be(expectedChannels);
            result.AudioSampleRate.Should().Be(expectedSampleRate);
            result.AudioCodec.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static byte[] CreateMp3Data(int sampleRate, int channels, int frameCount = 10)
    {
        using var ms = new MemoryStream();

        // ID3v2 header (10 bytes): 'ID3', version 2.3, flags 0, tag size 10 (syncsafe)
        ms.Write(Encoding.ASCII.GetBytes("ID3"));
        ms.WriteByte(3);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x0A });
        ms.Write(new byte[10]);

        byte sampleRateBits;
        int frameSize;
        int bitrate = 128000;
        switch (sampleRate)
        {
            case 48000:
                sampleRateBits = 0x01;
                frameSize = 144 * bitrate / 48000;
                break;
            case 32000:
                sampleRateBits = 0x02;
                frameSize = 144 * bitrate / 32000;
                break;
            case 44100:
            default:
                sampleRateBits = 0x00;
                frameSize = 144 * bitrate / 44100;
                break;
        }

        byte channelBits = channels == 1 ? (byte)0x03 : (byte)0x00;

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new byte[frameSize];
            frame[0] = 0xFF;
            frame[1] = 0xFB;
            frame[2] = (byte)((0x09 << 4) | (sampleRateBits << 2));
            frame[3] = (byte)(channelBits << 6);
            ms.Write(frame, 0, frame.Length);
        }

        return ms.ToArray();
    }

    [Test]
    public void Inspect_AviWithFilenameAudioHint_AppliesFilenameAudioHintWhenStreamAudioMissing()
    {
        var aviData = CreateAviHeader("XVID", 1280, 720, audioFormatTag: null);
        using var ms = new MemoryStream(aviData);

        var result = this.provider.Inspect(ms, "My.Movie.2005.DTS.5.1.avi");

        result.Should().NotBeNull();
        result.VideoCodec.Should().Be("Xvid / MPEG-4");
        result.AudioCodec.Should().Be("DTS");
        result.AudioChannels.Should().Be("5.1");
    }

    private static byte[] CreateMultiTrackMatroskaHeader(
        string docType,
        string videoCodecId,
        int width,
        int height,
        (string CodecId, int Channels)[] audioTracks,
        string[] subtitleCodecIds)
    {
        using var ms = new MemoryStream();

        // 1. EBML Header (0x1A45DFA3)
        using (var ebmlMs = new MemoryStream())
        {
            WriteEbmlString(ebmlMs, 0x4282, docType);
            var ebmlPayload = ebmlMs.ToArray();

            WriteId(ms, 0x1A45DFA3);
            WriteSize(ms, ebmlPayload.Length);
            ms.Write(ebmlPayload);
        }

        // 2. Segment (0x18538067)
        WriteId(ms, 0x18538067);
        WriteSize(ms, -1);

        // 3. Tracks (0x1654AE6B)
        WriteId(ms, 0x1654AE6B);
        WriteSize(ms, -1);

        // 4. Video TrackEntry (0xAE)
        if (!string.IsNullOrEmpty(videoCodecId))
        {
            WriteId(ms, 0xAE);
            WriteSize(ms, -1);
            WriteEbmlUInt(ms, 0x83, 1);
            WriteEbmlString(ms, 0x86, videoCodecId);

            WriteId(ms, 0xE0);
            WriteSize(ms, -1);
            WriteEbmlUInt(ms, 0xB0, (ulong)width);
            WriteEbmlUInt(ms, 0xBA, (ulong)height);
        }

        // 5. Audio TrackEntries
        if (audioTracks != null)
        {
            foreach (var (audioCodecId, channels) in audioTracks)
            {
                WriteId(ms, 0xAE);
                WriteSize(ms, -1);
                WriteEbmlUInt(ms, 0x83, 2);
                WriteEbmlString(ms, 0x86, audioCodecId);

                WriteId(ms, 0xE1);
                WriteSize(ms, -1);
                WriteEbmlUInt(ms, 0x9F, (ulong)channels);
            }
        }

        // 6. Subtitle TrackEntries
        if (subtitleCodecIds != null)
        {
            foreach (var subCodec in subtitleCodecIds)
            {
                WriteId(ms, 0xAE);
                WriteSize(ms, -1);
                WriteEbmlUInt(ms, 0x83, 17);
                WriteEbmlString(ms, 0x86, subCodec);
            }
        }

        return ms.ToArray();
    }

    private static byte[] CreateMatroskaHeader(string docType, string videoCodecId, int width, int height, string audioCodecId, int channels)
    {
        using var ms = new MemoryStream();

        // 1. EBML Header (0x1A45DFA3)
        using (var ebmlMs = new MemoryStream())
        {
            // DocType (0x4282)
            WriteEbmlString(ebmlMs, 0x4282, docType);
            var ebmlPayload = ebmlMs.ToArray();

            WriteId(ms, 0x1A45DFA3);
            WriteSize(ms, ebmlPayload.Length);
            ms.Write(ebmlPayload);
        }

        // 2. Segment (0x18538067)
        WriteId(ms, 0x18538067);
        WriteSize(ms, -1); // Unknown size

        // 3. Tracks (0x1654AE6B)
        WriteId(ms, 0x1654AE6B);
        WriteSize(ms, -1); // Unknown size

        // 4. Video TrackEntry (0xAE)
        WriteId(ms, 0xAE);
        WriteSize(ms, -1);

        // TrackType: Video (1)
        WriteEbmlUInt(ms, 0x83, 1);
        // CodecID
        WriteEbmlString(ms, 0x86, videoCodecId);

        // Video Settings (0xE0)
        WriteId(ms, 0xE0);
        WriteSize(ms, -1);
        WriteEbmlUInt(ms, 0xB0, (ulong)width);
        WriteEbmlUInt(ms, 0xBA, (ulong)height);

        // 5. Audio TrackEntry (0xAE)
        WriteId(ms, 0xAE);
        WriteSize(ms, -1);

        // TrackType: Audio (2)
        WriteEbmlUInt(ms, 0x83, 2);
        // CodecID
        WriteEbmlString(ms, 0x86, audioCodecId);

        // Audio Settings (0xE1)
        WriteId(ms, 0xE1);
        WriteSize(ms, -1);
        WriteEbmlUInt(ms, 0x9F, (ulong)channels);

        return ms.ToArray();
    }

    private static void WriteId(Stream stream, uint id)
    {
        if (id <= 0xFF)
        {
            stream.WriteByte((byte)id);
        }
        else if (id <= 0xFFFF)
        {
            stream.WriteByte((byte)(id >> 8));
            stream.WriteByte((byte)(id & 0xFF));
        }
        else
        {
            stream.WriteByte((byte)(id >> 24));
            stream.WriteByte((byte)((id >> 16) & 0xFF));
            stream.WriteByte((byte)((id >> 8) & 0xFF));
            stream.WriteByte((byte)(id & 0xFF));
        }
    }

    private static void WriteSize(Stream stream, long size)
    {
        if (size < 0)
        {
            // Unknown size: 0xFF
            stream.WriteByte(0xFF);
        }
        else if (size <= 0x7E)
        {
            stream.WriteByte((byte)(0x80 | size));
        }
        else if (size <= 0x3FFE)
        {
            stream.WriteByte((byte)(0x40 | (size >> 8)));
            stream.WriteByte((byte)(size & 0xFF));
        }
        else
        {
            stream.WriteByte((byte)(0x10 | (size >> 24)));
            stream.WriteByte((byte)((size >> 16) & 0xFF));
            stream.WriteByte((byte)((size >> 8) & 0xFF));
            stream.WriteByte((byte)(size & 0xFF));
        }
    }

    private static void WriteEbmlString(Stream stream, uint id, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteId(stream, id);
        WriteSize(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteEbmlUInt(Stream stream, uint id, ulong value)
    {
        byte[] bytes;
        if (value <= 0xFF)
        {
            bytes = new[] { (byte)value };
        }
        else if (value <= 0xFFFF)
        {
            bytes = new[] { (byte)(value >> 8), (byte)(value & 0xFF) };
        }
        else
        {
            bytes = new[]
            {
                (byte)(value >> 24),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF),
            };
        }

        WriteId(stream, id);
        WriteSize(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static byte[] CreateMp4Box(string type, byte[] payload)
    {
        using var ms = new MemoryStream();
        uint size = (uint)(payload.Length + 8);
        ms.WriteByte((byte)(size >> 24));
        ms.WriteByte((byte)((size >> 16) & 0xFF));
        ms.WriteByte((byte)((size >> 8) & 0xFF));
        ms.WriteByte((byte)(size & 0xFF));
        var typeBytes = Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes, 0, 4);
        ms.Write(payload, 0, payload.Length);
        return ms.ToArray();
    }

    private static byte[] CreateMp4LargeBox(string type, byte[] payload)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(1);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes, 0, 4);
        ulong totalSize = (ulong)(payload.Length + 16);
        for (int i = 7; i >= 0; i--)
        {
            ms.WriteByte((byte)((totalSize >> (i * 8)) & 0xFF));
        }

        ms.Write(payload, 0, payload.Length);
        return ms.ToArray();
    }

    private static byte[] CreateVisualSampleEntry(string format, ushort width, ushort height, string childBoxType = null)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[6], 0, 6);
        ms.Write(new byte[] { 0, 1 }, 0, 2);
        ms.Write(new byte[16], 0, 16);
        ms.WriteByte((byte)(width >> 8));
        ms.WriteByte((byte)(width & 0xFF));
        ms.WriteByte((byte)(height >> 8));
        ms.WriteByte((byte)(height & 0xFF));
        ms.Write(new byte[] { 0x00, 0x48, 0x00, 0x00, 0x00, 0x48, 0x00, 0x00, 0, 0, 0, 0, 0, 1 }, 0, 14);
        ms.Write(new byte[32], 0, 32);
        ms.Write(new byte[] { 0x00, 0x18, 0xFF, 0xFF }, 0, 4);

        if (!string.IsNullOrEmpty(childBoxType))
        {
            var child = CreateMp4Box(childBoxType, new byte[8]);
            ms.Write(child, 0, child.Length);
        }

        var payload = ms.ToArray();
        return CreateMp4Box(format, payload);
    }

    private static byte[] CreateVisualSampleEntryWithExtraBox(string format, ushort width, ushort height, byte[] extraBox)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[6], 0, 6);
        ms.Write(new byte[] { 0, 1 }, 0, 2);
        ms.Write(new byte[16], 0, 16);
        ms.WriteByte((byte)(width >> 8));
        ms.WriteByte((byte)(width & 0xFF));
        ms.WriteByte((byte)(height >> 8));
        ms.WriteByte((byte)(height & 0xFF));
        ms.Write(new byte[] { 0x00, 0x48, 0x00, 0x00, 0x00, 0x48, 0x00, 0x00, 0, 0, 0, 0, 0, 1 }, 0, 14);
        ms.Write(new byte[32], 0, 32);
        ms.Write(new byte[] { 0x00, 0x18, 0xFF, 0xFF }, 0, 4);

        if (extraBox != null && extraBox.Length > 0)
        {
            ms.Write(extraBox, 0, extraBox.Length);
        }

        var payload = ms.ToArray();
        return CreateMp4Box(format, payload);
    }

    private static byte[] CreateColrBox(ushort primaries, ushort transferCharacteristics, ushort matrixCoefficients)
    {
        using var ms = new MemoryStream();
        var nclxBytes = Encoding.ASCII.GetBytes("nclx");
        ms.Write(nclxBytes, 0, 4);
        ms.WriteByte((byte)(primaries >> 8));
        ms.WriteByte((byte)(primaries & 0xFF));
        ms.WriteByte((byte)(transferCharacteristics >> 8));
        ms.WriteByte((byte)(transferCharacteristics & 0xFF));
        ms.WriteByte((byte)(matrixCoefficients >> 8));
        ms.WriteByte((byte)(matrixCoefficients & 0xFF));
        ms.WriteByte(1); // full range flag
        return CreateMp4Box("colr", ms.ToArray());
    }

    private static byte[] CreateMatroskaHeaderWithColour(string docType, string videoCodecId, int width, int height, ulong transferChar, ulong primaries)
    {
        using var ms = new MemoryStream();

        // 1. EBML Header (0x1A45DFA3)
        using (var ebmlMs = new MemoryStream())
        {
            WriteEbmlString(ebmlMs, 0x4282, docType);
            var ebmlPayload = ebmlMs.ToArray();

            WriteId(ms, 0x1A45DFA3);
            WriteSize(ms, ebmlPayload.Length);
            ms.Write(ebmlPayload);
        }

        // 2. Segment (0x18538067)
        WriteId(ms, 0x18538067);
        WriteSize(ms, -1);

        // 3. Tracks (0x1654AE6B)
        WriteId(ms, 0x1654AE6B);
        WriteSize(ms, -1);

        // 4. Video TrackEntry (0xAE)
        WriteId(ms, 0xAE);
        WriteSize(ms, -1);

        WriteEbmlUInt(ms, 0x83, 1);
        WriteEbmlString(ms, 0x86, videoCodecId);

        // Video Settings (0xE0)
        WriteId(ms, 0xE0);
        WriteSize(ms, -1);
        WriteEbmlUInt(ms, 0xB0, (ulong)width);
        WriteEbmlUInt(ms, 0xBA, (ulong)height);

        // Colour Settings (0x55B0)
        WriteId(ms, 0x55B0);
        WriteSize(ms, -1);
        if (transferChar > 0)
        {
            WriteEbmlUInt(ms, 0x55B7, transferChar);
        }

        if (primaries > 0)
        {
            WriteEbmlUInt(ms, 0x55B8, primaries);
        }

        return ms.ToArray();
    }

    private static byte[] CreateAudioSampleEntry(string format, ushort channels, ushort sampleSize, uint sampleRate)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[6], 0, 6);
        ms.Write(new byte[] { 0, 1 }, 0, 2);
        ms.Write(new byte[8], 0, 8);
        ms.WriteByte((byte)(channels >> 8));
        ms.WriteByte((byte)(channels & 0xFF));
        ms.WriteByte((byte)(sampleSize >> 8));
        ms.WriteByte((byte)(sampleSize & 0xFF));
        ms.Write(new byte[4], 0, 4);
        uint srFixed = sampleRate << 16;
        ms.WriteByte((byte)(srFixed >> 24));
        ms.WriteByte((byte)((srFixed >> 16) & 0xFF));
        ms.WriteByte((byte)((srFixed >> 8) & 0xFF));
        ms.WriteByte((byte)(srFixed & 0xFF));

        var payload = ms.ToArray();
        return CreateMp4Box(format, payload);
    }

    private static byte[] CreateStsdBox(byte[] sampleEntry)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[4], 0, 4);
        ms.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);
        ms.Write(sampleEntry, 0, sampleEntry.Length);
        return CreateMp4Box("stsd", ms.ToArray());
    }

    private static byte[] CreateTrackBox(byte[] stsdBox)
    {
        var stbl = CreateMp4Box("stbl", stsdBox);
        var minf = CreateMp4Box("minf", stbl);
        var mdia = CreateMp4Box("mdia", minf);
        return CreateMp4Box("trak", mdia);
    }

    private static byte[] CreateMoovBox(params byte[][] trackBoxes)
    {
        using var ms = new MemoryStream();
        foreach (var trak in trackBoxes)
        {
            ms.Write(trak, 0, trak.Length);
        }

        return CreateMp4Box("moov", ms.ToArray());
    }

    private static byte[] CreateAviHeader(
        string videoFourCC,
        int width,
        int height,
        ushort? audioFormatTag = null,
        ushort audioChannels = 2,
        uint audioSampleRate = 44100,
        ushort audioBitsPerSample = 16)
    {
        using var ms = new MemoryStream();
        using var riffBody = new MemoryStream();
        using var hdrlBody = new MemoryStream();

        // avih chunk (56 bytes)
        using (var avihMs = new MemoryStream())
        {
            avihMs.Write(BitConverter.GetBytes(33333U)); // dwMicroSecPerFrame
            avihMs.Write(BitConverter.GetBytes(1000000U)); // dwMaxBytesPerSec
            avihMs.Write(BitConverter.GetBytes(0U)); // dwPaddingGranularity
            avihMs.Write(BitConverter.GetBytes(0U)); // dwFlags
            avihMs.Write(BitConverter.GetBytes(1000U)); // dwTotalFrames
            avihMs.Write(BitConverter.GetBytes(0U)); // dwInitialFrames
            avihMs.Write(BitConverter.GetBytes((uint)(audioFormatTag.HasValue ? 2 : 1))); // dwStreams
            avihMs.Write(BitConverter.GetBytes(0U)); // dwSuggestedBufferSize
            avihMs.Write(BitConverter.GetBytes((uint)width)); // dwWidth
            avihMs.Write(BitConverter.GetBytes((uint)height)); // dwHeight
            avihMs.Write(new byte[16]); // dwReserved

            var avihBytes = avihMs.ToArray();
            hdrlBody.Write(Encoding.ASCII.GetBytes("avih"));
            hdrlBody.Write(BitConverter.GetBytes((uint)avihBytes.Length));
            hdrlBody.Write(avihBytes);
        }

        // Video strl LIST
        using (var videoStrl = new MemoryStream())
        {
            // strh
            using (var strhMs = new MemoryStream())
            {
                strhMs.Write(Encoding.ASCII.GetBytes("vids"));
                var fccBytes = Encoding.ASCII.GetBytes((videoFourCC + "    ").Substring(0, 4));
                strhMs.Write(fccBytes);
                strhMs.Write(new byte[48]); // other strh fields

                var strhBytes = strhMs.ToArray();
                videoStrl.Write(Encoding.ASCII.GetBytes("strh"));
                videoStrl.Write(BitConverter.GetBytes((uint)strhBytes.Length));
                videoStrl.Write(strhBytes);
            }

            // strf (BITMAPINFOHEADER - 40 bytes)
            using (var strfMs = new MemoryStream())
            {
                strfMs.Write(BitConverter.GetBytes(40U)); // biSize
                strfMs.Write(BitConverter.GetBytes(width)); // biWidth
                strfMs.Write(BitConverter.GetBytes(height)); // biHeight
                strfMs.Write(BitConverter.GetBytes((ushort)1)); // biPlanes
                strfMs.Write(BitConverter.GetBytes((ushort)24)); // biBitCount
                var fccBytes = Encoding.ASCII.GetBytes((videoFourCC + "    ").Substring(0, 4));
                strfMs.Write(fccBytes); // biCompression
                strfMs.Write(BitConverter.GetBytes(width * height * 3)); // biSizeImage
                strfMs.Write(BitConverter.GetBytes(0)); // biXPelsPerMeter
                strfMs.Write(BitConverter.GetBytes(0)); // biYPelsPerMeter
                strfMs.Write(BitConverter.GetBytes(0U)); // biClrUsed
                strfMs.Write(BitConverter.GetBytes(0U)); // biClrImportant

                var strfBytes = strfMs.ToArray();
                videoStrl.Write(Encoding.ASCII.GetBytes("strf"));
                videoStrl.Write(BitConverter.GetBytes((uint)strfBytes.Length));
                videoStrl.Write(strfBytes);
            }

            var videoStrlBytes = videoStrl.ToArray();
            hdrlBody.Write(Encoding.ASCII.GetBytes("LIST"));
            hdrlBody.Write(BitConverter.GetBytes((uint)(videoStrlBytes.Length + 4)));
            hdrlBody.Write(Encoding.ASCII.GetBytes("strl"));
            hdrlBody.Write(videoStrlBytes);
        }

        // Audio strl LIST (if audioFormatTag present)
        if (audioFormatTag.HasValue)
        {
            using var audioStrl = new MemoryStream();

            // strh
            using (var strhMs = new MemoryStream())
            {
                strhMs.Write(Encoding.ASCII.GetBytes("auds"));
                strhMs.Write(new byte[4]); // fccHandler
                strhMs.Write(new byte[48]);

                var strhBytes = strhMs.ToArray();
                audioStrl.Write(Encoding.ASCII.GetBytes("strh"));
                audioStrl.Write(BitConverter.GetBytes((uint)strhBytes.Length));
                audioStrl.Write(strhBytes);
            }

            // strf (WAVEFORMATEX - 18 bytes)
            using (var strfMs = new MemoryStream())
            {
                strfMs.Write(BitConverter.GetBytes(audioFormatTag.Value)); // wFormatTag
                strfMs.Write(BitConverter.GetBytes(audioChannels)); // nChannels
                strfMs.Write(BitConverter.GetBytes(audioSampleRate)); // nSamplesPerSec
                uint avgBytesPerSec = audioSampleRate * audioChannels * (uint)(audioBitsPerSample / 8);
                strfMs.Write(BitConverter.GetBytes(avgBytesPerSec)); // nAvgBytesPerSec
                ushort blockAlign = (ushort)(audioChannels * (audioBitsPerSample / 8));
                ushort effectiveBlockAlign = blockAlign == 0 ? (ushort)1 : blockAlign;
                strfMs.Write(BitConverter.GetBytes(effectiveBlockAlign)); // nBlockAlign
                strfMs.Write(BitConverter.GetBytes(audioBitsPerSample)); // wBitsPerSample
                ushort cbSize = 0;
                strfMs.Write(BitConverter.GetBytes(cbSize)); // cbSize

                var strfBytes = strfMs.ToArray();
                audioStrl.Write(Encoding.ASCII.GetBytes("strf"));
                audioStrl.Write(BitConverter.GetBytes((uint)strfBytes.Length));
                audioStrl.Write(strfBytes);
            }

            var audioStrlBytes = audioStrl.ToArray();
            hdrlBody.Write(Encoding.ASCII.GetBytes("LIST"));
            hdrlBody.Write(BitConverter.GetBytes((uint)(audioStrlBytes.Length + 4)));
            hdrlBody.Write(Encoding.ASCII.GetBytes("strl"));
            hdrlBody.Write(audioStrlBytes);
        }

        var hdrlBytes = hdrlBody.ToArray();
        riffBody.Write(Encoding.ASCII.GetBytes("LIST"));
        riffBody.Write(BitConverter.GetBytes((uint)(hdrlBytes.Length + 4)));
        riffBody.Write(Encoding.ASCII.GetBytes("hdrl"));
        riffBody.Write(hdrlBytes);

        var riffPayload = riffBody.ToArray();
        ms.Write(Encoding.ASCII.GetBytes("RIFF"));
        ms.Write(BitConverter.GetBytes((uint)(riffPayload.Length + 4)));
        ms.Write(Encoding.ASCII.GetBytes("AVI "));
        ms.Write(riffPayload);

        return ms.ToArray();
    }

    private sealed class UnseekableStream : Stream
    {
        private readonly byte[] data;
        private int position;

        public UnseekableStream(byte[] data)
        {
            this.data = data;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.position >= this.data.Length)
            {
                return 0;
            }

            int toRead = Math.Min(count, this.data.Length - this.position);
            Array.Copy(this.data, this.position, buffer, offset, toRead);
            this.position += toRead;
            return toRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
