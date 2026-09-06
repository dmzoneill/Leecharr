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

    [Test]
    public void ApplyFilenameHints_WhenDimensionsAlreadySet_DoesNotOverwriteWithFilenameResolution()
    {
        var info = new MediaContainerInfo
        {
            Width = 1920,
            Height = 800,
            Resolution = "1080p",
            VideoCodec = "AV1",
            AudioCodec = "E-AC3",
            AudioChannels = "5.1",
            HdrFormat = "HDR10+",
        };

        TagLibInspectorProvider.ApplyFilenameHints(info, "Movie.Title.2024.2160p.UHD.HEVC.Atmos.TrueHD.DV.x265.mkv");

        info.Width.Should().Be(1920);
        info.Height.Should().Be(800);
        info.VideoCodec.Should().Be("AV1");
        info.AudioCodec.Should().Be("E-AC3");
        info.AudioChannels.Should().Be("5.1");
        info.HdrFormat.Should().Be("HDR10+");
    }

    [Test]
    public void ApplyFilenameHints_WhenPropertiesEmpty_PopulatesFromFilename()
    {
        var info = new MediaContainerInfo();

        TagLibInspectorProvider.ApplyFilenameHints(info, "Movie.Title.2024.1080p.x265.DTS.HDR.mkv");

        info.Width.Should().Be(1920);
        info.Height.Should().Be(1080);
        info.Resolution.Should().Be("1080p");
        info.VideoCodec.Should().Be("HEVC / H.265");
        info.AudioCodec.Should().Be("DTS");
        info.AudioChannels.Should().Be("5.1");
        info.HdrFormat.Should().Be("HDR10");
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
