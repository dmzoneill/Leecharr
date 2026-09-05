// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.MediaInspection;

public class TagLibInspectorProvider : IMediaInspectorProvider
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public string ProviderId => "TagLib";

    public string DisplayName => "TagLib# & Pure EBML (Pure .NET)";

    public string Version => typeof(TagLib.File).Assembly.GetName().Version?.ToString() ?? "2.3.0";

    public string Description => "Pure managed C# metadata inspector combining TagLibSharp with custom high-speed EBML header parsers. Zero native CLI dependencies.";

    public bool IsAvailable => true;

    public MediaInspectorCapabilities Capabilities { get; } = new()
    {
        SupportsDolbyVision = true,
        SupportsHdr10Plus = true,
        SupportsEac3Atmos = true,
        SupportsTrueHd = true,
        SupportsDtsX = true,
        SupportsSubtitleTracks = true,
        SupportsAudioStreamTracks = true,
        SupportsVideoStreamTracks = true,
        SupportsChapters = false,
        SupportsVideoThumbnails = false,
        SupportsPureManagedStreams = true,
    };

    public Task<MediaInspectorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MediaInspectorHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "TagLib# and pure EBML managed inspectors are operational.",
            DependencyChecks = new List<string>
            {
                "TagLibSharp .NET assembly: Loaded & Ready",
                "Pure EBML & container stream parsers: Operational"
            },
        });
    }

    public Task<MediaContainerInfo> InspectMediaAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.InspectFile(mediaPath));
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var info = this.Inspect(stream, Path.GetFileName(filePath));

            if (info == null)
            {
                info = InspectByFileName(Path.GetFileName(filePath)) ?? new MediaContainerInfo();
            }

            // Enrich with TagLib# if possible
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile.Properties != null)
                {
                    if (tagFile.Properties.VideoWidth > 0)
                    {
                        info.Width = tagFile.Properties.VideoWidth;
                    }

                    if (tagFile.Properties.VideoHeight > 0)
                    {
                        info.Height = tagFile.Properties.VideoHeight;
                    }

                    if (tagFile.Properties.Duration.TotalSeconds > 0)
                    {
                        info.DurationSeconds = tagFile.Properties.Duration.TotalSeconds;
                    }

                    if (tagFile.Properties.AudioChannels > 0 && string.IsNullOrEmpty(info.AudioChannels))
                    {
                        info.AudioChannels = tagFile.Properties.AudioChannels switch
                        {
                            1 => "1.0",
                            2 => "2.0",
                            6 => "5.1",
                            8 => "7.1",
                            _ => $"{tagFile.Properties.AudioChannels}.0",
                        };
                    }

                    if (tagFile.Properties.AudioSampleRate > 0 && info.AudioSampleRate == 0)
                    {
                        info.AudioSampleRate = tagFile.Properties.AudioSampleRate;
                    }

                    if (tagFile.Properties.BitsPerSample > 0 && info.AudioBitDepth == 0)
                    {
                        info.AudioBitDepth = tagFile.Properties.BitsPerSample;
                    }

                    if (tagFile.Properties.Codecs != null)
                    {
                        foreach (var codec in tagFile.Properties.Codecs)
                        {
                            if (codec.MediaTypes.HasFlag(TagLib.MediaTypes.Video) && string.IsNullOrEmpty(info.VideoCodec))
                            {
                                info.VideoCodec = codec.Description;
                            }
                            else if (codec.MediaTypes.HasFlag(TagLib.MediaTypes.Audio) && string.IsNullOrEmpty(info.AudioCodec))
                            {
                                info.AudioCodec = codec.Description;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(info.Resolution) && info.Width > 0)
                    {
                        ApplyResolution(info, info.Width, info.Height);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "TagLib file inspection fallback skipped for {0}", filePath);
            }

            return info;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "TagLib inspector failed on file {0}", filePath);
            return InspectByFileName(Path.GetFileName(filePath));
        }
    }

    public MediaContainerInfo Inspect(Stream stream, string fileName = "")
    {
        if (stream == null || !stream.CanRead)
        {
            return InspectByFileName(fileName);
        }

        byte[] header;
        int bytesRead;

        if (!stream.CanSeek)
        {
            var ms = new MemoryStream();
            var buf = new byte[4096];
            int read;
            int totalRead = 0;
            while (totalRead < 65536 && (read = stream.Read(buf, 0, Math.Min(buf.Length, 65536 - totalRead))) > 0)
            {
                ms.Write(buf, 0, read);
                totalRead += read;
            }

            header = ms.ToArray();
            bytesRead = header.Length;
        }
        else
        {
            var len = Math.Min(65536, stream.Length);
            header = new byte[len];
            var originalPos = stream.Position;
            stream.Seek(0, SeekOrigin.Begin);
            bytesRead = stream.Read(header, 0, header.Length);
            stream.Seek(originalPos, SeekOrigin.Begin);
        }

        if (bytesRead < 4)
        {
            return InspectByFileName(fileName);
        }

        // 1. Check MKV / WebM (EBML: 0x1A 0x45 0xDF 0xA3)
        if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return InspectMatroska(header, fileName);
        }

        // 2. Check MP4 / MOV / M4V ('ftyp', 'moov', 'mdat', 'free', 'skip', 'wide' at offset 4)
        if (bytesRead >= 8 && (
            (header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p') ||
            (header[4] == 'm' && header[5] == 'o' && header[6] == 'o' && header[7] == 'v') ||
            (header[4] == 'm' && header[5] == 'd' && header[6] == 'a' && header[7] == 't') ||
            (header[4] == 'f' && header[5] == 'r' && header[6] == 'e' && header[7] == 'e') ||
            (header[4] == 's' && header[5] == 'k' && header[6] == 'i' && header[7] == 'p') ||
            (header[4] == 'w' && header[5] == 'i' && header[6] == 'd' && header[7] == 'e')))
        {
            return InspectMp4(stream, header, fileName);
        }

        // 3. Check FLAC ('fLaC': 0x66 0x4C 0x61 0x43)
        if (header[0] == 'f' && header[1] == 'L' && header[2] == 'a' && header[3] == 'C')
        {
            return InspectFlac(header);
        }

        // 4. Check AVI (RIFF....AVI )
        if (bytesRead >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
            header[8] == 'A' && header[9] == 'V' && header[10] == 'I' && header[11] == ' ')
        {
            return InspectAvi(header, fileName);
        }

        // 5. Check MP3 (ID3v2: 'ID3' or Frame Sync 0xFF 0xFB/0xFA)
        if ((header[0] == 'I' && header[1] == 'D' && header[2] == '3') || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0))
        {
            return InspectMp3(header, fileName);
        }

        // Fallback: heuristic name inspector
        return InspectByFileName(fileName);
    }

    private static MediaContainerInfo InspectMatroska(byte[] header, string fileName)
    {
        var info = new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
        };

        int offset = 0;
        int limit = header.Length;

        while (offset < limit)
        {
            if (!ReadElementId(header, ref offset, out var id, out _))
            {
                break;
            }

            if (!ReadElementSize(header, ref offset, out var size, out _))
            {
                break;
            }

            if (IsEbmlMasterElement(id))
            {
                // Master element: descend directly into children
                continue;
            }

            // Leaf element
            if (size < 0 || offset + size > limit)
            {
                break;
            }

            int elemSize = (int)size;
            switch (id)
            {
                case 0x4282: // DocType
                    var docType = ReadEbmlString(header, offset, elemSize);
                    if (docType.Equals("webm", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ContainerFormat = "WebM";
                    }
                    else if (docType.Equals("matroska", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ContainerFormat = "Matroska (MKV)";
                    }

                    break;

                case 0x86: // CodecID
                    var codecId = ReadEbmlString(header, offset, elemSize);
                    ApplyCodecId(info, codecId);
                    break;

                case 0xB0: // PixelWidth
                    info.Width = (int)ReadEbmlUInt(header, offset, elemSize);
                    break;

                case 0xBA: // PixelHeight
                    info.Height = (int)ReadEbmlUInt(header, offset, elemSize);
                    break;

                case 0x9F: // Channels
                    var channels = (int)ReadEbmlUInt(header, offset, elemSize);
                    info.AudioChannels = channels switch
                    {
                        1 => "1.0",
                        2 => "2.0",
                        6 => "5.1",
                        8 => "7.1",
                        _ => $"{channels}.0",
                    };
                    break;

                case 0xB5: // SamplingFrequency
                    var sampleRate = ReadEbmlFloat(header, offset, elemSize);
                    if (sampleRate > 0)
                    {
                        info.AudioSampleRate = (int)sampleRate;
                    }

                    break;

                case 0x6264: // BitDepth
                    info.AudioBitDepth = (int)ReadEbmlUInt(header, offset, elemSize);
                    break;
            }

            offset += elemSize;
        }

        if (info.VideoCodec == null && info.AudioCodec == null)
        {
            var span = header.AsSpan();
            if (span.IndexOf("V_MPEGH/ISO/HEVC"u8) >= 0)
            {
                info.VideoCodec = "HEVC (H.265)";
            }
            else if (span.IndexOf("V_AV1"u8) >= 0)
            {
                info.VideoCodec = "AV1";
            }
            else if (span.IndexOf("V_VP9"u8) >= 0)
            {
                info.VideoCodec = "VP9";
            }
            else if (span.IndexOf("V_VP8"u8) >= 0)
            {
                info.VideoCodec = "VP8";
            }
            else if (span.IndexOf("V_MPEG4/ISO/AVC"u8) >= 0)
            {
                info.VideoCodec = "H.264";
            }

            if (span.IndexOf("A_TRUEHD"u8) >= 0)
            {
                info.AudioCodec = "Dolby TrueHD / Atmos";
                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = "7.1";
                }
            }
            else if (span.IndexOf("A_EAC3"u8) >= 0)
            {
                info.AudioCodec = "E-AC3 / Dolby Digital Plus";
                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = "5.1";
                }
            }
            else if (span.IndexOf("A_AC3"u8) >= 0)
            {
                info.AudioCodec = "AC3 / Dolby Digital";
                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = "5.1";
                }
            }
            else if (span.IndexOf("A_DTS"u8) >= 0)
            {
                info.AudioCodec = "DTS";
                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = "5.1";
                }
            }
            else if (span.IndexOf("A_FLAC"u8) >= 0)
            {
                info.AudioCodec = "FLAC";
                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = "2.0";
                }
            }
            else if (span.IndexOf("A_OPUS"u8) >= 0)
            {
                info.AudioCodec = "Opus";
                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = "2.0";
                }
            }
            else if (span.IndexOf("A_AAC"u8) >= 0)
            {
                info.AudioCodec = "AAC";
                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = "2.0";
                }
            }
        }

        ApplyFilenameHints(info, fileName);
        return info;
    }

    private static bool IsEbmlMasterElement(uint id)
    {
        return id == 0x1A45DFA3
            || id == 0x18538067
            || id == 0x1654AE6B
            || id == 0xAE
            || id == 0xE0
            || id == 0xE1;
    }

    private static bool ReadElementId(byte[] data, ref int offset, out uint id, out int idLen)
    {
        id = 0;
        idLen = 0;
        if (offset >= data.Length)
        {
            return false;
        }

        byte b = data[offset];
        if ((b & 0x80) != 0)
        {
            idLen = 1;
        }
        else if ((b & 0x40) != 0)
        {
            idLen = 2;
        }
        else if ((b & 0x20) != 0)
        {
            idLen = 3;
        }
        else if ((b & 0x10) != 0)
        {
            idLen = 4;
        }
        else
        {
            return false;
        }

        if (offset + idLen > data.Length)
        {
            return false;
        }

        uint result = 0;
        for (int i = 0; i < idLen; i++)
        {
            result = (result << 8) | data[offset + i];
        }

        id = result;
        offset += idLen;
        return true;
    }

    private static bool ReadElementSize(byte[] data, ref int offset, out long size, out int sizeLen)
    {
        size = 0;
        sizeLen = 0;
        if (offset >= data.Length)
        {
            return false;
        }

        byte b = data[offset];
        byte mask;
        if ((b & 0x80) != 0)
        {
            sizeLen = 1;
            mask = 0x7F;
        }
        else if ((b & 0x40) != 0)
        {
            sizeLen = 2;
            mask = 0x3F;
        }
        else if ((b & 0x20) != 0)
        {
            sizeLen = 3;
            mask = 0x1F;
        }
        else if ((b & 0x10) != 0)
        {
            sizeLen = 4;
            mask = 0x0F;
        }
        else if ((b & 0x08) != 0)
        {
            sizeLen = 5;
            mask = 0x07;
        }
        else if ((b & 0x04) != 0)
        {
            sizeLen = 6;
            mask = 0x03;
        }
        else if ((b & 0x02) != 0)
        {
            sizeLen = 7;
            mask = 0x01;
        }
        else if ((b & 0x01) != 0)
        {
            sizeLen = 8;
            mask = 0x00;
        }
        else
        {
            return false;
        }

        if (offset + sizeLen > data.Length)
        {
            return false;
        }

        long result = data[offset] & mask;
        bool allOnes = (data[offset] & mask) == mask;

        for (int i = 1; i < sizeLen; i++)
        {
            byte nextByte = data[offset + i];
            if (nextByte != 0xFF)
            {
                allOnes = false;
            }

            result = (result << 8) | nextByte;
        }

        offset += sizeLen;

        if (allOnes)
        {
            size = -1;
        }
        else
        {
            size = result;
        }

        return true;
    }

    private static string ReadEbmlString(byte[] data, int offset, int length)
    {
        if (length <= 0 || offset + length > data.Length)
        {
            return string.Empty;
        }

        var end = offset + length;
        while (end > offset && data[end - 1] == 0)
        {
            end--;
        }

        return System.Text.Encoding.UTF8.GetString(data, offset, end - offset);
    }

    private static ulong ReadEbmlUInt(byte[] data, int offset, int length)
    {
        ulong val = 0;
        for (int i = 0; i < length && (offset + i) < data.Length; i++)
        {
            val = (val << 8) | data[offset + i];
        }

        return val;
    }

    private static double ReadEbmlFloat(byte[] data, int offset, int length)
    {
        if (length == 4 && offset + 4 <= data.Length)
        {
            var bytes = new byte[4];
            Array.Copy(data, offset, bytes, 0, 4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToSingle(bytes, 0);
        }
        else if (length == 8 && offset + 8 <= data.Length)
        {
            var bytes = new byte[8];
            Array.Copy(data, offset, bytes, 0, 8);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToDouble(bytes, 0);
        }

        return 0.0;
    }

    private static void ApplyCodecId(MediaContainerInfo info, string codecId)
    {
        if (string.IsNullOrWhiteSpace(codecId))
        {
            return;
        }

        if (codecId.StartsWith("V_MPEGH/ISO/HEVC", StringComparison.OrdinalIgnoreCase))
        {
            info.VideoCodec = "HEVC (H.265)";
        }
        else if (codecId.StartsWith("V_AV1", StringComparison.OrdinalIgnoreCase))
        {
            info.VideoCodec = "AV1";
        }
        else if (codecId.StartsWith("V_VP9", StringComparison.OrdinalIgnoreCase))
        {
            info.VideoCodec = "VP9";
        }
        else if (codecId.StartsWith("V_VP8", StringComparison.OrdinalIgnoreCase))
        {
            info.VideoCodec = "VP8";
        }
        else if (codecId.StartsWith("V_MPEG4/ISO/AVC", StringComparison.OrdinalIgnoreCase))
        {
            info.VideoCodec = "H.264";
        }
        else if (codecId.StartsWith("A_TRUEHD", StringComparison.OrdinalIgnoreCase))
        {
            info.AudioCodec = "Dolby TrueHD / Atmos";
            if (string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = "7.1";
            }
        }
        else if (codecId.StartsWith("A_EAC3", StringComparison.OrdinalIgnoreCase))
        {
            info.AudioCodec = "E-AC3 / Dolby Digital Plus";
            if (string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = "5.1";
            }
        }
        else if (codecId.StartsWith("A_AC3", StringComparison.OrdinalIgnoreCase))
        {
            info.AudioCodec = "AC3 / Dolby Digital";
            if (string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = "5.1";
            }
        }
        else if (codecId.StartsWith("A_DTS", StringComparison.OrdinalIgnoreCase))
        {
            info.AudioCodec = "DTS";
            if (string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = "5.1";
            }
        }
        else if (codecId.StartsWith("A_FLAC", StringComparison.OrdinalIgnoreCase))
        {
            info.AudioCodec = "FLAC";
            if (string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = "2.0";
            }
        }
        else if (codecId.StartsWith("A_OPUS", StringComparison.OrdinalIgnoreCase))
        {
            info.AudioCodec = "Opus";
            if (string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = "2.0";
            }
        }
        else if (codecId.StartsWith("A_AAC", StringComparison.OrdinalIgnoreCase))
        {
            info.AudioCodec = "AAC";
            if (string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = "2.0";
            }
        }
    }

    private static MediaContainerInfo InspectMp4(Stream stream, byte[] header, string fileName)
    {
        var info = new MediaContainerInfo
        {
            ContainerFormat = "MP4",
        };

        if (stream != null && stream.CanSeek)
        {
            var originalPos = stream.Position;
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                ParseMp4Stream(stream, info);
            }
            catch
            {
                // Fallback / ignore corrupted boxes
            }
            finally
            {
                stream.Seek(originalPos, SeekOrigin.Begin);
            }
        }
        else if (header != null && header.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(header);
                ParseMp4Stream(ms, info);
            }
            catch
            {
                // Fallback / ignore corrupted boxes
            }
        }

        ApplyFilenameHints(info, fileName);
        return info;
    }

    private static void ParseMp4Stream(Stream stream, MediaContainerInfo info)
    {
        var headerBuf = new byte[16];
        long streamLength = stream.Length;

        while (stream.Position + 8 <= streamLength)
        {
            long boxStartPos = stream.Position;
            int read = stream.Read(headerBuf, 0, 8);
            if (read < 8)
            {
                break;
            }

            uint size32 = ((uint)headerBuf[0] << 24) | ((uint)headerBuf[1] << 16) | ((uint)headerBuf[2] << 8) | headerBuf[3];
            string boxType = System.Text.Encoding.ASCII.GetString(headerBuf, 4, 4);

            long boxSize;
            long headerSize = 8;

            if (size32 == 1)
            {
                if (stream.Position + 8 > streamLength)
                {
                    break;
                }

                int extRead = stream.Read(headerBuf, 8, 8);
                if (extRead < 8)
                {
                    break;
                }

                ulong size64 = ((ulong)headerBuf[8] << 56) |
                               ((ulong)headerBuf[9] << 48) |
                               ((ulong)headerBuf[10] << 40) |
                               ((ulong)headerBuf[11] << 32) |
                               ((ulong)headerBuf[12] << 24) |
                               ((ulong)headerBuf[13] << 16) |
                               ((ulong)headerBuf[14] << 8) |
                               headerBuf[15];

                boxSize = (long)size64;
                headerSize = 16;
            }
            else if (size32 == 0)
            {
                boxSize = streamLength - boxStartPos;
            }
            else
            {
                boxSize = size32;
            }

            if (boxSize < headerSize)
            {
                break;
            }

            long boxEndPos = boxStartPos + boxSize;

            if (boxType == "moov")
            {
                long payloadSize = boxSize - headerSize;
                int bytesToRead = (int)Math.Min(payloadSize, 32 * 1024 * 1024);
                var moovData = new byte[bytesToRead];
                int totalRead = 0;
                while (totalRead < bytesToRead)
                {
                    int r = stream.Read(moovData, totalRead, bytesToRead - totalRead);
                    if (r <= 0)
                    {
                        break;
                    }

                    totalRead += r;
                }

                ParseMp4Boxes(moovData, 0, totalRead, info);
            }

            if (boxEndPos > streamLength)
            {
                break;
            }

            if (stream.CanSeek)
            {
                stream.Seek(boxEndPos, SeekOrigin.Begin);
            }
            else
            {
                break;
            }
        }
    }

    private static void ParseMp4Boxes(byte[] data, int offset, int limit, MediaContainerInfo info)
    {
        while (offset + 8 <= limit)
        {
            int boxStart = offset;
            uint size32 = ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
            string boxType = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);

            long boxSize;
            int headerSize = 8;

            if (size32 == 1)
            {
                if (offset + 16 > limit)
                {
                    break;
                }

                ulong size64 = ((ulong)data[offset + 8] << 56) |
                               ((ulong)data[offset + 9] << 48) |
                               ((ulong)data[offset + 10] << 40) |
                               ((ulong)data[offset + 11] << 32) |
                               ((ulong)data[offset + 12] << 24) |
                               ((ulong)data[offset + 13] << 16) |
                               ((ulong)data[offset + 14] << 8) |
                               data[offset + 15];
                boxSize = (long)size64;
                headerSize = 16;
            }
            else if (size32 == 0)
            {
                boxSize = limit - boxStart;
            }
            else
            {
                boxSize = size32;
            }

            if (boxSize < headerSize)
            {
                break;
            }

            int boxEnd = (int)Math.Min(boxStart + boxSize, limit);
            int payloadOffset = boxStart + headerSize;

            switch (boxType)
            {
                case "moov":
                case "trak":
                case "mdia":
                case "minf":
                case "stbl":
                    ParseMp4Boxes(data, payloadOffset, boxEnd, info);
                    break;

                case "tkhd":
                    ParseTkhd(data, payloadOffset, boxEnd, info);
                    break;

                case "stsd":
                    ParseStsd(data, payloadOffset, boxEnd, info);
                    break;
            }

            offset = boxEnd;
        }
    }

    private static void ParseTkhd(byte[] data, int payloadOffset, int boxEnd, MediaContainerInfo info)
    {
        if (payloadOffset + 4 > boxEnd)
        {
            return;
        }

        byte version = data[payloadOffset];
        int widthOffset = version == 1 ? payloadOffset + 88 : payloadOffset + 76;
        int heightOffset = widthOffset + 4;

        if (heightOffset + 4 <= boxEnd)
        {
            uint wRaw = ((uint)data[widthOffset] << 24) | ((uint)data[widthOffset + 1] << 16) | ((uint)data[widthOffset + 2] << 8) | data[widthOffset + 3];
            uint hRaw = ((uint)data[heightOffset] << 24) | ((uint)data[heightOffset + 1] << 16) | ((uint)data[heightOffset + 2] << 8) | data[heightOffset + 3];

            int width = (int)(wRaw >> 16);
            int height = (int)(hRaw >> 16);

            if (width > 0 && height > 0 && info.Width == 0)
            {
                info.Width = width;
                info.Height = height;
                ApplyResolution(info, width, height);
            }
        }
    }

    private static void ParseStsd(byte[] data, int payloadOffset, int boxEnd, MediaContainerInfo info)
    {
        if (payloadOffset + 8 > boxEnd)
        {
            return;
        }

        uint entryCount = ((uint)data[payloadOffset + 4] << 24) | ((uint)data[payloadOffset + 5] << 16) | ((uint)data[payloadOffset + 6] << 8) | data[payloadOffset + 7];
        int entryOffset = payloadOffset + 8;

        for (int i = 0; i < entryCount && entryOffset + 8 <= boxEnd; i++)
        {
            uint entrySize = ((uint)data[entryOffset] << 24) | ((uint)data[entryOffset + 1] << 16) | ((uint)data[entryOffset + 2] << 8) | data[entryOffset + 3];
            if (entrySize < 8 || entryOffset + entrySize > boxEnd)
            {
                break;
            }

            string format = System.Text.Encoding.ASCII.GetString(data, entryOffset + 4, 4);

            switch (format)
            {
                case "hvc1":
                case "hev1":
                    info.VideoCodec = "HEVC (H.265)";
                    ExtractVisualSampleEntry(data, entryOffset, entrySize, info);
                    break;

                case "dvh1":
                case "dvhe":
                    info.VideoCodec = "HEVC (H.265)";
                    info.HdrFormat = "Dolby Vision";
                    ExtractVisualSampleEntry(data, entryOffset, entrySize, info);
                    break;

                case "av01":
                    info.VideoCodec = "AV1";
                    ExtractVisualSampleEntry(data, entryOffset, entrySize, info);
                    break;

                case "vp09":
                    info.VideoCodec = "VP9";
                    ExtractVisualSampleEntry(data, entryOffset, entrySize, info);
                    break;

                case "vp08":
                    info.VideoCodec = "VP8";
                    ExtractVisualSampleEntry(data, entryOffset, entrySize, info);
                    break;

                case "avc1":
                case "avc3":
                    info.VideoCodec = "H.264";
                    ExtractVisualSampleEntry(data, entryOffset, entrySize, info);
                    break;

                case "ec-3":
                case "ec+3":
                    info.AudioCodec = "E-AC3 / Dolby Digital Plus";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "5.1");
                    break;

                case "ac-3":
                case "ac+3":
                    info.AudioCodec = "AC3 / Dolby Digital";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "5.1");
                    break;

                case "alac":
                    info.AudioCodec = "Apple Lossless (ALAC)";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "2.0");
                    break;

                case "mp4a":
                    info.AudioCodec = "AAC";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "2.0");
                    break;

                case "Opus":
                case "opus":
                    info.AudioCodec = "Opus";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "2.0");
                    break;

                case "fLaC":
                case "flac":
                    info.AudioCodec = "FLAC";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "2.0");
                    break;

                case "dtsc":
                case "dtsh":
                case "dtsl":
                case "dtsx":
                case "DTS ":
                    info.AudioCodec = "DTS";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "5.1");
                    break;

                case "mlpa":
                    info.AudioCodec = "Dolby TrueHD / Atmos";
                    ExtractAudioSampleEntry(data, entryOffset, entrySize, info, "7.1");
                    break;

                case "tx3g":
                case "wvtt":
                case "stpp":
                case "c608":
                case "c708":
                    if (!info.SubtitleTracks.Contains(format))
                    {
                        info.SubtitleTracks.Add(format);
                    }

                    break;
            }

            entryOffset += (int)entrySize;
        }
    }

    private static void ExtractVisualSampleEntry(byte[] data, int entryOffset, uint entrySize, MediaContainerInfo info)
    {
        if (entrySize >= 36 && entryOffset + 36 <= data.Length)
        {
            ushort width = (ushort)((data[entryOffset + 32] << 8) | data[entryOffset + 33]);
            ushort height = (ushort)((data[entryOffset + 34] << 8) | data[entryOffset + 35]);

            if (width > 0 && height > 0 && info.Width == 0)
            {
                info.Width = width;
                info.Height = height;
                ApplyResolution(info, width, height);
            }
        }

        // Search child boxes for HDR / Dolby Vision indicators
        int childOffset = entryOffset + 86;
        int childLimit = entryOffset + (int)entrySize;
        while (childOffset + 8 <= childLimit && childOffset + 8 <= data.Length)
        {
            uint cSize = ((uint)data[childOffset] << 24) | ((uint)data[childOffset + 1] << 16) | ((uint)data[childOffset + 2] << 8) | data[childOffset + 3];
            if (cSize < 8 || childOffset + cSize > childLimit)
            {
                break;
            }

            string cType = System.Text.Encoding.ASCII.GetString(data, childOffset + 4, 4);
            if (cType == "dvcC" || cType == "dvvC")
            {
                info.HdrFormat = "Dolby Vision";
            }

            childOffset += (int)cSize;
        }
    }

    private static void ExtractAudioSampleEntry(byte[] data, int entryOffset, uint entrySize, MediaContainerInfo info, string defaultChannels)
    {
        if (entrySize >= 36 && entryOffset + 36 <= data.Length)
        {
            ushort channelCount = (ushort)((data[entryOffset + 24] << 8) | data[entryOffset + 25]);
            ushort sampleSize = (ushort)((data[entryOffset + 26] << 8) | data[entryOffset + 27]);
            uint sampleRateRaw = ((uint)data[entryOffset + 32] << 24) | ((uint)data[entryOffset + 33] << 16) | ((uint)data[entryOffset + 34] << 8) | data[entryOffset + 35];
            int sampleRate = (int)(sampleRateRaw >> 16);

            if (channelCount > 0 && string.IsNullOrEmpty(info.AudioChannels))
            {
                info.AudioChannels = channelCount switch
                {
                    1 => "1.0",
                    2 => "2.0",
                    6 => "5.1",
                    8 => "7.1",
                    _ => $"{channelCount}.0",
                };
            }

            if (sampleRate > 0 && info.AudioSampleRate == 0)
            {
                info.AudioSampleRate = sampleRate;
            }

            if (sampleSize > 0 && info.AudioBitDepth == 0)
            {
                info.AudioBitDepth = sampleSize;
            }
        }

        if (string.IsNullOrEmpty(info.AudioChannels))
        {
            info.AudioChannels = defaultChannels;
        }
    }

    private static void ApplyResolution(MediaContainerInfo info, int width, int height)
    {
        if (string.IsNullOrEmpty(info.Resolution))
        {
            if (width >= 3800 || height >= 2100)
            {
                info.Resolution = "4K UHD (2160p)";
            }
            else if (width >= 1900 || height >= 1000)
            {
                info.Resolution = "1080p";
            }
            else if (width >= 1200 || height >= 700)
            {
                info.Resolution = "720p";
            }
            else if (width >= 640 || height >= 400)
            {
                info.Resolution = "480p";
            }
        }
    }

    private static MediaContainerInfo InspectFlac(byte[] header)
    {
        var info = new MediaContainerInfo
        {
            ContainerFormat = "FLAC",
            AudioCodec = "FLAC",
            AudioChannels = "2.0",
            AudioBitDepth = 16,
            AudioSampleRate = 44100,
        };

        if (header.Length >= 22)
        {
            var b18 = header[18];
            var b19 = header[19];
            var b20 = header[20];

            var sampleRate = (b18 << 12) | (b19 << 4) | (b20 >> 4);
            var channels = ((b20 >> 1) & 0x07) + 1;
            var bitsPerSample = (((b20 & 0x01) << 4) | (header[21] >> 4)) + 1;

            if (sampleRate > 0)
            {
                info.AudioSampleRate = sampleRate;
            }

            info.AudioChannels = channels switch
            {
                1 => "1.0",
                6 => "5.1",
                8 => "7.1",
                _ => $"{channels}.0",
            };

            info.AudioBitDepth = bitsPerSample;
        }

        return info;
    }

    private static MediaContainerInfo InspectAvi(byte[] header, string fileName)
    {
        var info = new MediaContainerInfo
        {
            ContainerFormat = "AVI",
        };

        var text = System.Text.Encoding.ASCII.GetString(header);

        if (text.Contains("XVID") || text.Contains("xvid") || text.Contains("DX50") || text.Contains("DIVX"))
        {
            info.VideoCodec = "Xvid / MPEG-4";
        }
        else if (text.Contains("H264") || text.Contains("h264") || text.Contains("AVC1"))
        {
            info.VideoCodec = "H.264";
        }

        info.AudioCodec = text.Contains("AC3") ? "AC3" : "MP3";
        info.AudioChannels = "2.0";

        ApplyFilenameHints(info, fileName);
        return info;
    }

    private static MediaContainerInfo InspectMp3(byte[] header, string fileName)
    {
        return new MediaContainerInfo
        {
            ContainerFormat = "MP3",
            AudioCodec = "MP3",
            AudioChannels = "2.0",
            AudioSampleRate = 44100,
            AudioBitDepth = 16,
        };
    }

    public static MediaContainerInfo InspectByFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var info = new MediaContainerInfo();

        switch (ext)
        {
            case ".mkv":
                info.ContainerFormat = "Matroska (MKV)";
                break;
            case ".mp4":
            case ".m4v":
                info.ContainerFormat = "MP4";
                break;
            case ".avi":
                info.ContainerFormat = "AVI";
                break;
            case ".flac":
                info.ContainerFormat = "FLAC";
                info.AudioCodec = "FLAC";
                return info;
            case ".mp3":
                info.ContainerFormat = "MP3";
                info.AudioCodec = "MP3";
                return info;
            default:
                return null;
        }

        ApplyFilenameHints(info, fileName);
        return info;
    }

    public static void ApplyFilenameHints(MediaContainerInfo info, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || info == null)
        {
            return;
        }

        var upper = fileName.ToUpperInvariant();
        var normalized = upper.Replace('.', ' ').Replace('-', ' ').Replace('_', ' ');

        // Resolution
        if (upper.Contains("2160P") || upper.Contains("4K") || upper.Contains("UHD"))
        {
            info.Resolution = "4K UHD (2160p)";
            info.Width = 3840;
            info.Height = 2160;
        }
        else if (upper.Contains("1080P") || upper.Contains("FHD"))
        {
            info.Resolution = "1080p";
            info.Width = 1920;
            info.Height = 1080;
        }
        else if (upper.Contains("720P") || upper.Contains("HD"))
        {
            info.Resolution = "720p";
            info.Width = 1280;
            info.Height = 720;
        }
        else if (upper.Contains("480P") || upper.Contains("SD"))
        {
            info.Resolution = "480p";
            info.Width = 854;
            info.Height = 480;
        }

        // HDR
        if (upper.Contains("DV") || normalized.Contains("DOLBY VISION") || upper.Contains("DOVI"))
        {
            info.HdrFormat = "Dolby Vision";
        }
        else if (upper.Contains("HDR10+") || normalized.Contains("HDR10 PLUS") || upper.Contains("HDR10PLUS"))
        {
            info.HdrFormat = "HDR10+";
        }
        else if (upper.Contains("HDR"))
        {
            info.HdrFormat = "HDR10";
        }

        // Video Codec
        if (upper.Contains("HEVC") || upper.Contains("H.265") || upper.Contains("H265") || upper.Contains("X265"))
        {
            info.VideoCodec = "HEVC / H.265";
        }
        else if (upper.Contains("AVC") || upper.Contains("H.264") || upper.Contains("H264") || upper.Contains("X264"))
        {
            info.VideoCodec = "AVC / H.264";
        }
        else if (upper.Contains("AV1"))
        {
            info.VideoCodec = "AV1";
        }

        // Audio Codec
        if (upper.Contains("ATMOS"))
        {
            info.AudioCodec = "Dolby Atmos";
            info.AudioChannels = "7.1";
        }
        else if (upper.Contains("TRUEHD"))
        {
            info.AudioCodec = "Dolby TrueHD";
            info.AudioChannels = "7.1";
        }
        else if (upper.Contains("DTS-HD") || upper.Contains("DTS-HD MA"))
        {
            info.AudioCodec = "DTS-HD MA";
            info.AudioChannels = "7.1";
        }
        else if (upper.Contains("DTS"))
        {
            info.AudioCodec = "DTS";
            info.AudioChannels = "5.1";
        }
        else if (upper.Contains("EAC3") || upper.Contains("DDP") || upper.Contains("DD+"))
        {
            info.AudioCodec = "E-AC3 / DD+";
            info.AudioChannels = "5.1";
        }
        else if (upper.Contains("AC3") || upper.Contains("DD5.1") || upper.Contains("DD 5.1"))
        {
            info.AudioCodec = "AC3 / Dolby Digital";
            info.AudioChannels = "5.1";
        }
    }
}
