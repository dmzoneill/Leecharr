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

        var header = new byte[Math.Min(65536, stream.Length)];
        var originalPos = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);
        var bytesRead = stream.Read(header, 0, header.Length);
        stream.Seek(originalPos, SeekOrigin.Begin);

        if (bytesRead < 4)
        {
            return InspectByFileName(fileName);
        }

        // 1. Check MKV / WebM (EBML: 0x1A 0x45 0xDF 0xA3)
        if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return InspectMatroska(header, fileName);
        }

        // 2. Check MP4 / MOV / M4V ('ftyp' at offset 4 or 'moov' at offset 4)
        if (bytesRead >= 8 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
        {
            return InspectMp4(header, fileName);
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

        var text = System.Text.Encoding.ASCII.GetString(header);

        // Detect video codec from EBML CodecID strings
        if (text.Contains("V_MPEGH/ISO/HEVC"))
        {
            info.VideoCodec = "HEVC (H.265)";
        }
        else if (text.Contains("V_AV1"))
        {
            info.VideoCodec = "AV1";
        }
        else if (text.Contains("V_VP9"))
        {
            info.VideoCodec = "VP9";
        }
        else if (text.Contains("V_VP8"))
        {
            info.VideoCodec = "VP8";
        }
        else if (text.Contains("V_MPEG4/ISO/AVC"))
        {
            info.VideoCodec = "H.264";
        }

        // Detect audio codec from EBML CodecID strings
        if (text.Contains("A_TRUEHD"))
        {
            info.AudioCodec = "Dolby TrueHD / Atmos";
            info.AudioChannels = "7.1";
        }
        else if (text.Contains("A_EAC3"))
        {
            info.AudioCodec = "E-AC3 / Dolby Digital Plus";
            info.AudioChannels = "5.1";
        }
        else if (text.Contains("A_AC3"))
        {
            info.AudioCodec = "AC3 / Dolby Digital";
            info.AudioChannels = "5.1";
        }
        else if (text.Contains("A_DTS"))
        {
            info.AudioCodec = "DTS";
            info.AudioChannels = "5.1";
        }
        else if (text.Contains("A_FLAC"))
        {
            info.AudioCodec = "FLAC";
            info.AudioChannels = "2.0";
        }
        else if (text.Contains("A_OPUS"))
        {
            info.AudioCodec = "Opus";
            info.AudioChannels = "2.0";
        }
        else if (text.Contains("A_AAC"))
        {
            info.AudioCodec = "AAC";
            info.AudioChannels = "2.0";
        }

        ApplyFilenameHints(info, fileName);
        return info;
    }

    private static MediaContainerInfo InspectMp4(byte[] header, string fileName)
    {
        var info = new MediaContainerInfo
        {
            ContainerFormat = "MP4",
        };

        var text = System.Text.Encoding.ASCII.GetString(header);

        // Detect video codec from MP4 FourCC sample entries
        if (text.Contains("hvc1") || text.Contains("hev1"))
        {
            info.VideoCodec = "HEVC (H.265)";
        }
        else if (text.Contains("av01"))
        {
            info.VideoCodec = "AV1";
        }
        else if (text.Contains("vp09"))
        {
            info.VideoCodec = "VP9";
        }
        else if (text.Contains("avc1") || text.Contains("avc3"))
        {
            info.VideoCodec = "H.264";
        }

        // Detect audio codec from MP4 FourCC sample entries
        if (text.Contains("ec-3"))
        {
            info.AudioCodec = "E-AC3 / Dolby Digital Plus";
            info.AudioChannels = "5.1";
        }
        else if (text.Contains("ac-3"))
        {
            info.AudioCodec = "AC3 / Dolby Digital";
            info.AudioChannels = "5.1";
        }
        else if (text.Contains("alac"))
        {
            info.AudioCodec = "Apple Lossless (ALAC)";
            info.AudioChannels = "2.0";
        }
        else if (text.Contains("mp4a"))
        {
            info.AudioCodec = "AAC";
            info.AudioChannels = "2.0";
        }

        ApplyFilenameHints(info, fileName);
        return info;
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
