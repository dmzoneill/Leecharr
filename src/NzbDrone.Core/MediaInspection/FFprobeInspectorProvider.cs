using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Common;

namespace NzbDrone.Core.MediaInspection;

public class FFprobeInspectorProvider : IMediaInspectorProvider
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly TagLibInspectorProvider _fallbackProvider = new();

    public string ProviderId => "FFprobe";
    public string DisplayName => "FFprobe / FFmpeg (CLI / Multi-Stream)";
    public string Version => "7.0.2 (FFmpeg/FFprobe CLI)";
    public string Description => "FFmpeg multimedia analyzer extracting precise frame dimensions, HDR color metadata, multi-channel layouts, and stream indexes.";
    public bool IsAvailable => FindBinary() != null;

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
        SupportsChapters = true,
        SupportsVideoThumbnails = true,
        SupportsPureManagedStreams = false
    };

    public Task<MediaInspectorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        var binary = FindBinary();
        if (binary != null)
        {
            return Task.FromResult(new MediaInspectorHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"FFprobe CLI executable found at {binary}.",
                DependencyChecks = new List<string> { $"FFprobe binary: {binary}" }
            });
        }

        return Task.FromResult(new MediaInspectorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "FFprobe executable not found on PATH or standard locations.",
            Warnings = new List<string> { "Install ffmpeg/ffprobe or set FFPROBE_PATH environment variable." }
        });
    }

    public async Task<MediaContainerInfo> InspectMediaAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return null;
        }

        var binary = FindBinary();
        if (binary == null)
        {
            return _fallbackProvider.InspectFile(mediaPath);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = $"-v quiet -print_format json -show_format -show_streams \"{mediaPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var parsed = ParseFFprobeJson(stdout, Path.GetFileName(mediaPath));
                if (parsed != null)
                {
                    return parsed;
                }
            }

            return _fallbackProvider.InspectFile(mediaPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FFprobe failed to inspect media: {0}", mediaPath);
            return _fallbackProvider.InspectFile(mediaPath);
        }
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        return InspectMediaAsync(filePath).GetAwaiter().GetResult();
    }

    public MediaContainerInfo Inspect(Stream stream, string fileName = "")
    {
        return _fallbackProvider.Inspect(stream, fileName);
    }

    public static MediaContainerInfo ParseFFprobeJson(string json, string fileName = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var info = new MediaContainerInfo();

            // 1. Format section
            if (root.TryGetProperty("format", out var formatElement))
            {
                if (formatElement.TryGetProperty("format_name", out var fnProp))
                {
                    var fn = fnProp.GetString() ?? string.Empty;
                    if (fn.Contains("matroska", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ContainerFormat = "Matroska (MKV)";
                    }
                    else if (fn.Contains("mp4", StringComparison.OrdinalIgnoreCase) || fn.Contains("mov", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ContainerFormat = "MP4";
                    }
                    else if (fn.Contains("avi", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ContainerFormat = "AVI";
                    }
                    else if (fn.Contains("flac", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ContainerFormat = "FLAC";
                    }
                    else if (fn.Contains("mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ContainerFormat = "MP3";
                    }
                    else
                    {
                        info.ContainerFormat = fn;
                    }
                }

                if (formatElement.TryGetProperty("duration", out var durProp) &&
                    double.TryParse(durProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSec))
                {
                    info.DurationSeconds = durationSec;
                }
            }

            // 2. Streams section
            if (root.TryGetProperty("streams", out var streamsElement) && streamsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamsElement.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out var typeProp))
                    {
                        continue;
                    }

                    var codecType = typeProp.GetString();

                    if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(info.VideoCodec) && stream.TryGetProperty("codec_name", out var vCodec))
                        {
                            var vc = vCodec.GetString() ?? string.Empty;
                            info.VideoCodec = vc.ToUpperInvariant() switch
                            {
                                "HEVC" => "HEVC / H.265",
                                "H264" => "AVC / H.264",
                                "AV1" => "AV1",
                                "VP9" => "VP9",
                                "MPEG4" => "MPEG-4",
                                _ => vc
                            };
                        }

                        if (stream.TryGetProperty("width", out var wProp) && wProp.TryGetInt32(out var width))
                        {
                            info.Width = width;
                        }

                        if (stream.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out var height))
                        {
                            info.Height = height;
                        }

                        // Check HDR indicators
                        var colorTransfer = stream.TryGetProperty("color_transfer", out var ctProp) ? ctProp.GetString() : string.Empty;

                        if (stream.TryGetProperty("side_data_list", out var sideDataArray) && sideDataArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var sideData in sideDataArray.EnumerateArray())
                            {
                                if (sideData.TryGetProperty("side_data_type", out var sdtProp))
                                {
                                    var sdt = sdtProp.GetString() ?? string.Empty;
                                    if (sdt.Contains("DOVI", StringComparison.OrdinalIgnoreCase) || sdt.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase))
                                    {
                                        info.HdrFormat = "Dolby Vision";
                                        break;
                                    }

                                    if (sdt.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) || sdt.Contains("HDR Dynamic Metadata", StringComparison.OrdinalIgnoreCase))
                                    {
                                        info.HdrFormat = "HDR10+";
                                    }
                                }
                            }
                        }

                        if (info.HdrFormat == "SDR" && !string.IsNullOrEmpty(colorTransfer))
                        {
                            if (colorTransfer.Contains("smpte2084", StringComparison.OrdinalIgnoreCase))
                            {
                                info.HdrFormat = "HDR10";
                            }
                            else if (colorTransfer.Contains("arib-std-b67", StringComparison.OrdinalIgnoreCase))
                            {
                                info.HdrFormat = "HLG";
                            }
                        }
                    }
                    else if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(info.AudioCodec) && stream.TryGetProperty("codec_name", out var aCodec))
                        {
                            var ac = aCodec.GetString() ?? string.Empty;
                            info.AudioCodec = ac.ToUpperInvariant() switch
                            {
                                "EAC3" => "E-AC3 / DD+",
                                "AC3" => "AC3 / Dolby Digital",
                                "TRUEHD" => "Dolby TrueHD",
                                "DTS" => "DTS",
                                "FLAC" => "FLAC",
                                "AAC" => "AAC",
                                "MP3" => "MP3",
                                "OPUS" => "Opus",
                                _ => ac
                            };
                        }

                        if (string.IsNullOrEmpty(info.AudioChannels) && stream.TryGetProperty("channels", out var chanProp) &&
                            chanProp.TryGetInt32(out var channels))
                        {
                            info.AudioChannels = channels switch
                            {
                                1 => "1.0",
                                2 => "2.0",
                                6 => "5.1",
                                8 => "7.1",
                                _ => $"{channels}.0"
                            };
                        }

                        if (info.AudioSampleRate == 0 && stream.TryGetProperty("sample_rate", out var srProp))
                        {
                            if (srProp.ValueKind == JsonValueKind.Number && srProp.TryGetInt32(out var srInt))
                            {
                                info.AudioSampleRate = srInt;
                            }
                            else if (srProp.ValueKind == JsonValueKind.String && int.TryParse(srProp.GetString(), out var srParsed))
                            {
                                info.AudioSampleRate = srParsed;
                            }
                        }

                        if (info.AudioBitDepth == 0 && stream.TryGetProperty("bits_per_raw_sample", out var bprsProp))
                        {
                            if (bprsProp.ValueKind == JsonValueKind.Number && bprsProp.TryGetInt32(out var bdInt))
                            {
                                info.AudioBitDepth = bdInt;
                            }
                            else if (bprsProp.ValueKind == JsonValueKind.String && int.TryParse(bprsProp.GetString(), out var bdParsed))
                            {
                                info.AudioBitDepth = bdParsed;
                            }
                        }
                    }
                    else if (string.Equals(codecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                    {
                        var subLabel = string.Empty;
                        if (stream.TryGetProperty("tags", out var tagsElem))
                        {
                            if (tagsElem.TryGetProperty("language", out var langProp))
                            {
                                subLabel = langProp.GetString();
                            }

                            if (tagsElem.TryGetProperty("title", out var titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
                            {
                                subLabel = string.IsNullOrEmpty(subLabel) ? titleProp.GetString() : $"{subLabel} ({titleProp.GetString()})";
                            }
                        }

                        if (string.IsNullOrEmpty(subLabel) && stream.TryGetProperty("codec_name", out var sCodec))
                        {
                            subLabel = sCodec.GetString();
                        }

                        if (!string.IsNullOrWhiteSpace(subLabel))
                        {
                            info.SubtitleTracks.Add(subLabel);
                        }
                    }
                }
            }

            // Derive resolution
            if (info.Width >= 3800 || info.Height >= 2100)
            {
                info.Resolution = "4K UHD (2160p)";
            }
            else if (info.Width >= 1900 || info.Height >= 1000)
            {
                info.Resolution = "1080p";
            }
            else if (info.Width >= 1200 || info.Height >= 700)
            {
                info.Resolution = "720p";
            }
            else if (info.Height >= 480)
            {
                info.Resolution = "480p";
            }

            TagLibInspectorProvider.ApplyFilenameHints(info, fileName);
            return info;
        }
        catch
        {
            return null;
        }
    }

    private static string FindBinary()
    {
        return CliProcessDiscovery.FindExecutable("ffprobe", "FFPROBE_PATH", new[] { "/usr/bin/ffprobe", "/usr/local/bin/ffprobe" });
    }
}
