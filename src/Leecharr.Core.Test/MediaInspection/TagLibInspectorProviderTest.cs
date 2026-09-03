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
