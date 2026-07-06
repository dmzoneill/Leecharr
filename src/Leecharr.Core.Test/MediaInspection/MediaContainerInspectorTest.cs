using System.IO;
using NUnit.Framework;
using NzbDrone.Core.MediaInspection;

namespace Leecharr.Core.Test.MediaInspection;

[TestFixture]
public class MediaContainerInspectorTest
{
    private MediaContainerInspector _inspector;

    [SetUp]
    public void SetUp()
    {
        _inspector = new MediaContainerInspector();
    }

    [Test]
    public void should_inspect_flac_header()
    {
        // Construct a valid FLAC header: 'fLaC' + 4-byte block header + STREAMINFO (34 bytes)
        var flacData = new byte[42];
        flacData[0] = (byte)'f';
        flacData[1] = (byte)'L';
        flacData[2] = (byte)'a';
        flacData[3] = (byte)'C';
        flacData[4] = 0x00; // METADATA_BLOCK_HEADER: STREAMINFO

        // Set sample rate = 96000 (0x17700), channels = 2 (0b001 -> 2ch), bits per sample = 24 (0b10111 -> 24bit)
        // b18 = (96000 >> 12) = 0x17
        // b19 = (96000 >> 4) & 0xFF = 0x70
        // b20 = ((96000 & 0xF) << 4) | (1 << 1) | (23 >> 4) = 0x00 | 0x02 | 0x01 = 0x03
        // b21 = (23 & 0x0F) << 4 = 0x70
        flacData[18] = 0x17;
        flacData[19] = 0x70;
        flacData[20] = 0x03;
        flacData[21] = 0x70;

        using var ms = new MemoryStream(flacData);
        var info = _inspector.Inspect(ms, "track01.flac");

        Assert.That(info, Is.Not.Null);
        Assert.That(info.ContainerFormat, Is.EqualTo("FLAC"));
        Assert.That(info.AudioCodec, Is.EqualTo("FLAC"));
        Assert.That(info.AudioSampleRate, Is.EqualTo(96000));
        Assert.That(info.AudioChannels, Is.EqualTo("2.0"));
        Assert.That(info.AudioBitDepth, Is.EqualTo(24));
    }

    [Test]
    public void should_extract_4k_hdr_atmos_from_scene_filename()
    {
        var dummyHeader = new byte[8];
        dummyHeader[4] = (byte)'f';
        dummyHeader[5] = (byte)'t';
        dummyHeader[6] = (byte)'y';
        dummyHeader[7] = (byte)'p';

        using var ms = new MemoryStream(dummyHeader);
        var info = _inspector.Inspect(ms, "Dune.Part.Two.2024.2160p.UHD.HDR.DV.TrueHD.Atmos.7.1.x265-FLUX.mp4");

        Assert.That(info, Is.Not.Null);
        Assert.That(info.ContainerFormat, Is.EqualTo("MP4"));
        Assert.That(info.Resolution, Is.EqualTo("4K UHD (2160p)"));
        Assert.That(info.Width, Is.EqualTo(3840));
        Assert.That(info.Height, Is.EqualTo(2160));
        Assert.That(info.HdrFormat, Is.EqualTo("Dolby Vision"));
        Assert.That(info.VideoCodec, Is.EqualTo("HEVC / H.265"));
        Assert.That(info.AudioCodec, Is.EqualTo("Dolby Atmos"));
        Assert.That(info.AudioChannels, Is.EqualTo("7.1"));
    }
}
