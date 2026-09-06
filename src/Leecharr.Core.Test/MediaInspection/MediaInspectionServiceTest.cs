// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaInspection;

namespace Leecharr.Core.Test.MediaInspection;

[TestFixture]
public class MediaInspectionServiceTest
{
    private MediaContainerInspector inspector = null!;
    private TagLibInspectorProvider tagLibProvider = null!;

    [SetUp]
    public void SetUp()
    {
        this.inspector = new MediaContainerInspector();
        this.tagLibProvider = new TagLibInspectorProvider();
    }

    [Test]
    public void Inspect_WhenStreamIsNull_FallsBackToFilename()
    {
        var info = this.inspector.Inspect(null!, "Movie.2024.1080p.mkv");
        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        info.Resolution.Should().Be("1080p");
    }

    [Test]
    public void Inspect_WhenStreamHasLessThan4Bytes_FallsBackToFilename()
    {
        using var ms = new MemoryStream(new byte[] { 0x01, 0x02 });
        var info = this.inspector.Inspect(ms, "Movie.2024.720p.mp4");
        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("MP4");
        info.Resolution.Should().Be("720p");
    }

    [TestCase("V_MPEGH/ISO/HEVC", "HEVC (H.265)")]
    [TestCase("V_AV1", "AV1")]
    [TestCase("V_VP9", "VP9")]
    [TestCase("V_VP8", "VP8")]
    [TestCase("V_MPEG4/ISO/AVC", "H.264")]
    public void Inspect_MkvEbmlVideoCodecs_DetectsCorrectly(string codecId, string expectedCodec)
    {
        var header = CreateMkvEbmlHeader(codecId);
        using var ms = new MemoryStream(header);

        var info = this.inspector.Inspect(ms, "sample.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        info.VideoCodec.Should().Be(expectedCodec);
    }

    [TestCase("A_TRUEHD", "Dolby TrueHD / Atmos", "7.1")]
    [TestCase("A_EAC3", "E-AC3 / Dolby Digital Plus", "5.1")]
    [TestCase("A_AC3", "AC3 / Dolby Digital", "5.1")]
    [TestCase("A_DTS", "DTS", "5.1")]
    [TestCase("A_FLAC", "FLAC", "2.0")]
    [TestCase("A_OPUS", "Opus", "2.0")]
    [TestCase("A_AAC", "AAC", "2.0")]
    public void Inspect_MkvEbmlAudioCodecs_DetectsCorrectly(string codecId, string expectedCodec, string expectedChannels)
    {
        var header = CreateMkvEbmlHeader(codecId);
        using var ms = new MemoryStream(header);

        var info = this.inspector.Inspect(ms, "sample.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        info.AudioCodec.Should().Be(expectedCodec);
        info.AudioChannels.Should().Be(expectedChannels);
    }

    private static byte[] CreateMkvEbmlHeader(string payload)
    {
        var bytes = new byte[256];

        // EBML magic: 0x1A 0x45 0xDF 0xA3
        bytes[0] = 0x1A;
        bytes[1] = 0x45;
        bytes[2] = 0xDF;
        bytes[3] = 0xA3;

        var payloadBytes = Encoding.ASCII.GetBytes(payload);
        Array.Copy(payloadBytes, 0, bytes, 4, Math.Min(payloadBytes.Length, bytes.Length - 4));
        return bytes;
    }

    [TestCase("hvc1", "HEVC (H.265)")]
    [TestCase("hev1", "HEVC (H.265)")]
    [TestCase("av01", "AV1")]
    [TestCase("vp09", "VP9")]
    [TestCase("avc1", "H.264")]
    [TestCase("avc3", "H.264")]
    public void Inspect_Mp4BoxVideoCodecs_DetectsCorrectly(string fourCc, string expectedCodec)
    {
        var header = CreateMp4Header(fourCc);
        using var ms = new MemoryStream(header);

        var info = this.inspector.Inspect(ms, "sample.mp4");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("MP4");
        info.VideoCodec.Should().Be(expectedCodec);
    }

    [TestCase("ec-3", "E-AC3 / Dolby Digital Plus", "5.1")]
    [TestCase("ac-3", "AC3 / Dolby Digital", "5.1")]
    [TestCase("alac", "Apple Lossless (ALAC)", "2.0")]
    [TestCase("mp4a", "AAC", "2.0")]
    public void Inspect_Mp4BoxAudioCodecs_DetectsCorrectly(string fourCc, string expectedCodec, string expectedChannels)
    {
        var header = CreateMp4Header(fourCc);
        using var ms = new MemoryStream(header);

        var info = this.inspector.Inspect(ms, "sample.mp4");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("MP4");
        info.AudioCodec.Should().Be(expectedCodec);
        info.AudioChannels.Should().Be(expectedChannels);
    }

    private static byte[] CreateMp4Header(string payload)
    {
        var bytes = new byte[256];

        // 'ftyp' box at offset 4
        bytes[4] = (byte)'f';
        bytes[5] = (byte)'t';
        bytes[6] = (byte)'y';
        bytes[7] = (byte)'p';

        var payloadBytes = Encoding.ASCII.GetBytes(payload);
        Array.Copy(payloadBytes, 0, bytes, 8, Math.Min(payloadBytes.Length, bytes.Length - 8));
        return bytes;
    }

    [TestCase("XVID", "Xvid / MPEG-4")]
    [TestCase("xvid", "Xvid / MPEG-4")]
    [TestCase("DX50", "Xvid / MPEG-4")]
    [TestCase("DIVX", "Xvid / MPEG-4")]
    [TestCase("H264", "H.264")]
    [TestCase("h264", "H.264")]
    [TestCase("AVC1", "H.264")]
    public void Inspect_AviHeaderVideoCodecs_DetectsCorrectly(string fourCc, string expectedCodec)
    {
        var header = CreateAviHeader(fourCc);
        using var ms = new MemoryStream(header);

        var info = this.inspector.Inspect(ms, "sample.avi");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("AVI");
        info.VideoCodec.Should().Be(expectedCodec);
    }

    [Test]
    public void Inspect_AviWithAc3_DetectsAc3Audio()
    {
        var header = CreateAviHeader("XVID AC3");
        using var ms = new MemoryStream(header);

        var info = this.inspector.Inspect(ms, "sample.avi");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("AVI");
        info.AudioCodec.Should().Be("AC3");
        info.AudioChannels.Should().Be("2.0");
    }

    [Test]
    public void Inspect_AviWithoutAc3_DefaultsToMp3Audio()
    {
        var header = CreateAviHeader("XVID");
        using var ms = new MemoryStream(header);

        var info = this.inspector.Inspect(ms, "sample.avi");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("AVI");
        info.AudioCodec.Should().Be("MP3");
        info.AudioChannels.Should().Be("2.0");
    }

    private static byte[] CreateAviHeader(string payload)
    {
        var bytes = new byte[256];

        // RIFF....AVI
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'A';
        bytes[9] = (byte)'V';
        bytes[10] = (byte)'I';
        bytes[11] = (byte)' ';

        var payloadBytes = Encoding.ASCII.GetBytes(payload);
        Array.Copy(payloadBytes, 0, bytes, 12, Math.Min(payloadBytes.Length, bytes.Length - 12));
        return bytes;
    }

    [TestCase(44100, 2, 16, "2.0")]
    [TestCase(48000, 6, 24, "5.1")]
    [TestCase(96000, 8, 24, "7.1")]
    [TestCase(192000, 1, 24, "1.0")]
    public void Inspect_FlacStreamInfo_ParsesMetadataAccurately(int sampleRate, int channels, int bitDepth, string expectedChannels)
    {
        var data = new byte[64];

        // 'fLaC' magic
        data[0] = (byte)'f';
        data[1] = (byte)'L';
        data[2] = (byte)'a';
        data[3] = (byte)'C';
        data[4] = 0x00; // METADATA_BLOCK_HEADER

        // STREAMINFO layout starting at byte 18:
        // 20 bits sample rate, 3 bits channels-1, 5 bits (bitsPerSample-1)
        var chMinus1 = channels - 1;
        var bpsMinus1 = bitDepth - 1;

        data[18] = (byte)((sampleRate >> 12) & 0xFF);
        data[19] = (byte)((sampleRate >> 4) & 0xFF);
        data[20] = (byte)(((sampleRate & 0x0F) << 4) | ((chMinus1 & 0x07) << 1) | ((bpsMinus1 >> 4) & 0x01));
        data[21] = (byte)((bpsMinus1 & 0x0F) << 4);

        using var ms = new MemoryStream(data);
        var info = this.inspector.Inspect(ms, "track.flac");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("FLAC");
        info.AudioCodec.Should().Be("FLAC");
        info.AudioSampleRate.Should().Be(sampleRate);
        info.AudioChannels.Should().Be(expectedChannels);
        info.AudioBitDepth.Should().Be(bitDepth);
    }

    [Test]
    public void Inspect_Mp3WithId3Header_DetectsMp3()
    {
        var data = new byte[64];
        data[0] = (byte)'I';
        data[1] = (byte)'D';
        data[2] = (byte)'3';

        using var ms = new MemoryStream(data);
        var info = this.inspector.Inspect(ms, "music.mp3");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("MP3");
        info.AudioCodec.Should().Be("MP3");
        info.AudioChannels.Should().Be("2.0");
        info.AudioSampleRate.Should().Be(44100);
        info.AudioBitDepth.Should().Be(16);
    }

    [Test]
    public void Inspect_Mp3WithFrameSync_DetectsMp3()
    {
        var data = new byte[64];
        data[0] = 0xFF;
        data[1] = 0xFB; // Frame sync

        using var ms = new MemoryStream(data);
        var info = this.inspector.Inspect(ms, "track.mp3");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("MP3");
        info.AudioCodec.Should().Be("MP3");
        info.AudioChannels.Should().Be("2.0");
    }

    [TestCase("Movie.2024.2160p.UHD.mkv", "4K UHD (2160p)", 3840, 2160)]
    [TestCase("Movie.2024.4K.HEVC.mkv", "4K UHD (2160p)", 3840, 2160)]
    [TestCase("Movie.2024.1080p.FHD.mkv", "1080p", 1920, 1080)]
    [TestCase("Movie.2024.720p.HD.mkv", "720p", 1280, 720)]
    [TestCase("Movie.2024.480p.SD.mkv", "480p", 854, 480)]
    public void ApplyFilenameHints_ResolutionClassification_SetsCorrectDimensions(
        string filename, string expectedResolution, int expectedWidth, int expectedHeight)
    {
        var info = new MediaContainerInfo();
        TagLibInspectorProvider.ApplyFilenameHints(info, filename);

        info.Resolution.Should().Be(expectedResolution);
        info.Width.Should().Be(expectedWidth);
        info.Height.Should().Be(expectedHeight);
    }

    [TestCase("Movie.2024.2160p.DV.mkv", "Dolby Vision")]
    [TestCase("Movie.2024.2160p.DOLBY.VISION.mkv", "Dolby Vision")]
    [TestCase("Movie.2024.2160p.DoVi.mkv", "Dolby Vision")]
    [TestCase("Movie.2024.2160p.HDR10+.mkv", "HDR10+")]
    [TestCase("Movie.2024.2160p.HDR10plus.mkv", "HDR10+")]
    [TestCase("Movie.2024.2160p.HDR.mkv", "HDR10")]
    public void ApplyFilenameHints_HdrProfiles_ExtractsCorrectHdrFormat(string filename, string expectedHdr)
    {
        var info = new MediaContainerInfo();
        TagLibInspectorProvider.ApplyFilenameHints(info, filename);

        info.HdrFormat.Should().Be(expectedHdr);
    }

    [TestCase("Movie.2024.Atmos.mkv", "Dolby Atmos", "7.1")]
    [TestCase("Movie.2024.TrueHD.mkv", "Dolby TrueHD", "7.1")]
    [TestCase("Movie.2024.DTS-HD.MA.mkv", "DTS-HD MA", "7.1")]
    [TestCase("Movie.2024.DTS.5.1.mkv", "DTS", "5.1")]
    [TestCase("Movie.2024.EAC3.mkv", "E-AC3 / DD+", "5.1")]
    [TestCase("Movie.2024.DDP.mkv", "E-AC3 / DD+", "5.1")]
    [TestCase("Movie.2024.DD5.1.mkv", "AC3 / Dolby Digital", "5.1")]
    [TestCase("Movie.2024.AC3.mkv", "AC3 / Dolby Digital", "5.1")]
    public void ApplyFilenameHints_AudioLayout_ExtractsCorrectAudioAndChannels(
        string filename, string expectedAudioCodec, string expectedChannels)
    {
        var info = new MediaContainerInfo();
        TagLibInspectorProvider.ApplyFilenameHints(info, filename);

        info.AudioCodec.Should().Be(expectedAudioCodec);
        info.AudioChannels.Should().Be(expectedChannels);
    }

    [Test]
    public void ParseMediaInfoJson_4kDolbyVisionAtmos_ParsesCompleteStreamDetails()
    {
        var json = @"{
          ""media"": {
            ""track"": [
              {
                ""@type"": ""General"",
                ""Format"": ""Matroska"",
                ""Duration"": ""7200.500""
              },
              {
                ""@type"": ""Video"",
                ""Format"": ""HEVC"",
                ""Width"": ""3840"",
                ""Height"": ""2160"",
                ""HDR_Format_Commercial"": ""Dolby Vision"",
                ""HDR_Format"": ""SMPTE ST 2086""
              },
              {
                ""@type"": ""Audio"",
                ""Format_Commercial_IfAny"": ""Dolby Atmos"",
                ""Format"": ""E-AC-3 JOC"",
                ""Channels"": ""8"",
                ""SamplingRate"": ""48000"",
                ""BitDepth"": ""24""
              },
              {
                ""@type"": ""Text"",
                ""Language"": ""en"",
                ""Title"": ""English SDH"",
                ""Format"": ""SubRip""
              }
            ]
          }
        }";

        var info = MediaInfoInspectorProvider.ParseMediaInfoJson(json, "sample.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska");
        info.VideoCodec.Should().Be("HEVC");
        info.Width.Should().Be(3840);
        info.Height.Should().Be(2160);
        info.Resolution.Should().Be("4K UHD (2160p)");
        info.HdrFormat.Should().Be("Dolby Vision");
        info.AudioCodec.Should().Be("Dolby Atmos");
        info.AudioChannels.Should().Be("7.1");
        info.AudioSampleRate.Should().Be(48000);
        info.AudioBitDepth.Should().Be(24);
        info.DurationSeconds.Should().BeApproximately(7200.5, 0.01);
        info.SubtitleTracks.Should().Contain("en (English SDH)");
    }

    [Test]
    public void ParseMediaInfoJson_1080pHdr10Plus_Classifies1080pAndHdr10Plus()
    {
        var json = @"{
          ""media"": {
            ""track"": [
              {
                ""@type"": ""Video"",
                ""Format"": ""AVC"",
                ""Width"": ""1920"",
                ""Height"": ""1080"",
                ""HDR_Format"": ""HDR10+""
              },
              {
                ""@type"": ""Audio"",
                ""Format"": ""AC-3"",
                ""Channels"": ""6"",
                ""SamplingRate"": ""48000""
              }
            ]
          }
        }";

        var info = MediaInfoInspectorProvider.ParseMediaInfoJson(json, "sample.mp4");

        info.Should().NotBeNull();
        info.Resolution.Should().Be("1080p");
        info.HdrFormat.Should().Be("HDR10+");
        info.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void ParseMediaInfoJson_WhenMalformedJson_ReturnsNull()
    {
        var info = MediaInfoInspectorProvider.ParseMediaInfoJson("{ invalid json }", "sample.mkv");
        info.Should().BeNull();
    }

    [TestCase("sample.mkv", "Matroska (MKV)")]
    [TestCase("sample.mp4", "MP4")]
    [TestCase("sample.m4v", "MP4")]
    [TestCase("sample.avi", "AVI")]
    [TestCase("sample.flac", "FLAC")]
    [TestCase("sample.mp3", "MP3")]
    public void InspectByFileName_SupportedExtensions_ReturnsContainerFormat(string fileName, string expectedContainer)
    {
        var info = TagLibInspectorProvider.InspectByFileName(fileName);
        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be(expectedContainer);
    }

    [TestCase("sample.txt")]
    [TestCase("sample.exe")]
    [TestCase("sample.iso")]
    [TestCase("")]
    [TestCase(null)]
    public void InspectByFileName_UnsupportedExtensionsOrNull_ReturnsNull(string fileName)
    {
        var info = TagLibInspectorProvider.InspectByFileName(fileName);
        info.Should().BeNull();
    }

    [Test]
    public void ParseFFprobeJson_Smpte2084Transfer_DetectsHdr10()
    {
        var json = @"{
          ""streams"": [
            {
              ""codec_type"": ""video"",
              ""codec_name"": ""hevc"",
              ""width"": 3840,
              ""height"": 2160,
              ""color_transfer"": ""smpte2084""
            }
          ],
          ""format"": { ""format_name"": ""matroska,webm"", ""duration"": ""120.0"" }
        }";

        var info = FFprobeInspectorProvider.ParseFFprobeJson(json, "sample.mkv");
        info.Should().NotBeNull();
        info.HdrFormat.Should().Be("HDR10");
        info.Resolution.Should().Be("4K UHD (2160p)");
    }

    [Test]
    public void ParseFFprobeJson_AribStdB67Transfer_DetectsHlg()
    {
        var json = @"{
          ""streams"": [
            {
              ""codec_type"": ""video"",
              ""codec_name"": ""hevc"",
              ""width"": 3840,
              ""height"": 2160,
              ""color_transfer"": ""arib-std-b67""
            }
          ],
          ""format"": { ""format_name"": ""matroska,webm"", ""duration"": ""120.0"" }
        }";

        var info = FFprobeInspectorProvider.ParseFFprobeJson(json, "sample.mkv");
        info.Should().NotBeNull();
        info.HdrFormat.Should().Be("HLG");
    }

    [Test]
    public void ParseFFprobeJson_WhenSecondaryStereoAudioFirst_PrefersPrimaryMultiChannelAudio()
    {
        var json = @"{
          ""streams"": [
            {
              ""codec_type"": ""video"",
              ""codec_name"": ""hevc"",
              ""width"": 3840,
              ""height"": 2160
            },
            {
              ""codec_type"": ""audio"",
              ""codec_name"": ""aac"",
              ""channels"": 2,
              ""sample_rate"": ""44100""
            },
            {
              ""codec_type"": ""audio"",
              ""codec_name"": ""truehd"",
              ""channels"": 8,
              ""sample_rate"": ""48000"",
              ""bits_per_raw_sample"": ""24""
            }
          ],
          ""format"": { ""format_name"": ""matroska,webm"", ""duration"": ""120.0"" }
        }";

        var info = FFprobeInspectorProvider.ParseFFprobeJson(json, "sample.mkv");
        info.Should().NotBeNull();
        info.AudioCodec.Should().Be("Dolby TrueHD");
        info.AudioChannels.Should().Be("7.1");
        info.AudioSampleRate.Should().Be(48000);
        info.AudioBitDepth.Should().Be(24);
    }

    [Test]
    public void ParseMediaInfoJson_WhenSecondaryStereoAudioFirst_PrefersPrimaryMultiChannelAudio()
    {
        var json = @"{
          ""media"": {
            ""track"": [
              {
                ""@type"": ""Video"",
                ""Format"": ""HEVC"",
                ""Width"": ""3840"",
                ""Height"": ""2160""
              },
              {
                ""@type"": ""Audio"",
                ""Format"": ""AAC"",
                ""Channels"": ""2"",
                ""SamplingRate"": ""44100""
              },
              {
                ""@type"": ""Audio"",
                ""Format_Commercial_IfAny"": ""Dolby TrueHD"",
                ""Format"": ""TrueHD"",
                ""Channels"": ""8"",
                ""SamplingRate"": ""48000"",
                ""BitDepth"": ""24""
              }
            ]
          }
        }";

        var info = MediaInfoInspectorProvider.ParseMediaInfoJson(json, "sample.mkv");
        info.Should().NotBeNull();
        info.AudioCodec.Should().Be("Dolby TrueHD");
        info.AudioChannels.Should().Be("7.1");
        info.AudioSampleRate.Should().Be(48000);
        info.AudioBitDepth.Should().Be(24);
    }

    [Test]
    public void Inspect_EbmlMatroskaStream_CalculatesDurationAndParsesTracks()
    {
        var provider = new TagLibInspectorProvider();

        // Construct EBML stream: EBML Header (0x1A45DFA3) + Segment (0x18538067, size -1/unknown) + Info (0x1549A966) + Duration (0x4489, 4 bytes float = 3600.0)
        var ms = new MemoryStream();
        // 1. EBML header: ID 0x1A45DFA3, size 0
        ms.Write(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x80 });
        // 2. Segment: ID 0x18538067, size unknown (0xFF)
        ms.Write(new byte[] { 0x18, 0x53, 0x80, 0x67, 0xFF });
        // 3. Info container: ID 0x1549A966, size unknown (0xFF)
        ms.Write(new byte[] { 0x15, 0x49, 0xA9, 0x66, 0xFF });
        // 4. TimecodeScale: ID 0x2AD7B1, size 3 -> 1000000 (0x0F4240)
        ms.Write(new byte[] { 0x2A, 0xD7, 0xB1, 0x83, 0x0F, 0x42, 0x40 });
        // 5. Duration: ID 0x4489, size 4 -> float 3600000.0f (3600s with 1ms TimecodeScale)
        var durBytes = BitConverter.GetBytes(3600000.0f);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(durBytes);
        }

        ms.Write(new byte[] { 0x44, 0x89, 0x84 });
        ms.Write(durBytes);

        // 6. Tracks: ID 0x1654AE6B, size unknown
        ms.Write(new byte[] { 0x16, 0x54, 0xAE, 0x6B, 0xFF });
        // 7. TrackEntry: ID 0xAE, size unknown
        ms.Write(new byte[] { 0xAE, 0xFF });
        // 8. VideoSettings: ID 0xE0, size unknown
        ms.Write(new byte[] { 0xE0, 0xFF });
        // 9. PixelWidth: ID 0xB0, size 2 -> 1920 (0x0780)
        ms.Write(new byte[] { 0xB0, 0x82, 0x07, 0x80 });
        // 10. PixelHeight: ID 0xBA, size 2 -> 1080 (0x0438)
        ms.Write(new byte[] { 0xBA, 0x82, 0x04, 0x38 });

        ms.Position = 0;
        var info = provider.Inspect(ms, "test.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        info.DurationSeconds.Should().BeApproximately(3600.0, 0.01);
        info.Width.Should().Be(1920);
        info.Height.Should().Be(1080);
        info.Resolution.Should().Be("1080p");
    }
}
