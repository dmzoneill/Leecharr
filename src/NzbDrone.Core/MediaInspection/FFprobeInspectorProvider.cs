// Copyright (c) FeedItOut. All rights reserved.

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

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class FFprobeInspectorProvider : IMediaInspectorProvider
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly TagLibInspectorProvider fallbackProvider = new();
    private readonly string customBinaryPath;
    private readonly TimeSpan executionTimeout;
    private static readonly string[] DefaultSearchPaths = ["/usr/bin/ffprobe", "/usr/local/bin/ffprobe"];

    public FFprobeInspectorProvider()
        : this(null, TimeSpan.FromSeconds(60))
    {
    }

    internal FFprobeInspectorProvider(string customBinaryPath, TimeSpan? executionTimeout = null)
    {
        this.customBinaryPath = customBinaryPath;
        this.executionTimeout = executionTimeout ?? TimeSpan.FromSeconds(60);
    }

    public string ProviderId => "FFprobe";

    public string DisplayName => "FFprobe / FFmpeg (CLI / Multi-Stream)";

    public string Version => "7.0.2 (FFmpeg/FFprobe CLI)";

    public string Description => "FFmpeg multimedia analyzer extracting precise frame dimensions, HDR color metadata, multi-channel layouts, and stream indexes.";

    public bool IsAvailable => this.FindBinary() != null;

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
        SupportsPureManagedStreams = false,
    };

    public async Task<MediaInspectorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        var binary = this.FindBinary();
        if (binary == null)
        {
            return new MediaInspectorHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = "FFprobe executable not found on PATH or standard locations.",
                Warnings = new List<string> { "Install ffmpeg/ffprobe or set FFPROBE_PATH environment variable." },
            };
        }

        var versionLine = await this.ProbeBinaryVersionAsync(binary, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(versionLine))
        {
            return new MediaInspectorHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"FFprobe CLI operational at {binary}. {versionLine}",
                DependencyChecks = new List<string> { $"FFprobe binary: {binary}", versionLine },
            };
        }

        return new MediaInspectorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = $"FFprobe executable at {binary} failed to start or returned a non-zero exit code.",
            DependencyChecks = new List<string> { $"FFprobe binary: {binary}" },
            Warnings = new List<string>
            {
                "Verify ffprobe runs successfully (for example `ffprobe -version`) and required shared libraries are installed.",
            },
        };
    }

    public async Task<MediaContainerInfo> InspectMediaAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return null;
        }

        var binary = this.FindBinary();
        if (binary == null)
        {
            return this.fallbackProvider.InspectFile(mediaPath);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = binary,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("quiet");
            startInfo.ArgumentList.Add("-print_format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-show_format");
            startInfo.ArgumentList.Add("-show_streams");
            startInfo.ArgumentList.Add(mediaPath);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(this.executionTimeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token));
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to kill FFprobe process after cancellation");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    this.logger.Debug("FFprobe inspection cancelled for {0}", mediaPath);
                }
                else
                {
                    this.logger.Warn("FFprobe execution timed out after {0} seconds for {1}", this.executionTimeout.TotalSeconds, mediaPath);
                }

                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stderr) && process.ExitCode != 0)
            {
                this.logger.Warn("FFprobe reported errors on stderr: {0}", stderr);
            }

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var parsed = ParseFFprobeJson(stdout, Path.GetFileName(mediaPath));
                if (parsed != null)
                {
                    return parsed;
                }
            }

            return this.fallbackProvider.InspectFile(mediaPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "FFprobe failed to inspect media: {0}", mediaPath);
            return this.fallbackProvider.InspectFile(mediaPath);
        }
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        return this.InspectMediaAsync(filePath).GetAwaiter().GetResult();
    }

    public MediaContainerInfo Inspect(Stream stream, string fileName = "")
    {
        return this.InspectStreamAsync(stream, fileName).GetAwaiter().GetResult();
    }

    private async Task<MediaContainerInfo> InspectStreamAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        if (stream == null || !stream.CanRead)
        {
            return this.fallbackProvider.Inspect(stream, fileName);
        }

        if (this.FindBinary() == null)
        {
            return this.fallbackProvider.Inspect(stream, fileName);
        }

        long? originalPosition = null;
        if (stream.CanSeek)
        {
            originalPosition = stream.Position;
            stream.Seek(0, SeekOrigin.Begin);
        }

        string tempPath = null;
        try
        {
            tempPath = await MaterializeStreamToTempFileAsync(stream, fileName, cancellationToken);
            return await this.InspectMediaAsync(tempPath, cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "FFprobe failed to inspect stream for {0}", fileName);
            if (originalPosition.HasValue && stream.CanSeek)
            {
                stream.Seek(originalPosition.Value, SeekOrigin.Begin);
            }

            return this.fallbackProvider.Inspect(stream, fileName);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to delete temporary FFprobe stream file {0}", tempPath);
                }
            }
        }
    }

    private static async Task<string> MaterializeStreamToTempFileAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"leecharr_ffprobe_{Guid.NewGuid():N}{extension}");
        await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        return tempPath;
    }

    public static MediaContainerInfo ParseFFprobeJson(string json, string fileName = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var info = new MediaContainerInfo();
            var isInterlaced = false;

            // 1. Format section
            if (root.TryGetProperty("format", out var formatElement) && formatElement.ValueKind == JsonValueKind.Object)
            {
                if (formatElement.TryGetProperty("format_name", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
                {
                    var fn = fnProp.GetString() ?? string.Empty;
                    info.ContainerFormat = MapFfprobeFormatName(fn, fileName);
                }

                if (formatElement.TryGetProperty("duration", out var durProp) &&
                    TryParseFfprobeDurationSeconds(durProp, out var formatDuration) &&
                    formatDuration > 0)
                {
                    info.DurationSeconds = formatDuration;
                }

                if (formatElement.TryGetProperty("tags", out var formatTags) && formatTags.ValueKind == JsonValueKind.Object)
                {
                    ApplyFfprobeFormatTags(info, formatTags);
                }
            }

            var maxStreamDurationSeconds = 0.0;

            // 2. Streams section
            if (root.TryGetProperty("streams", out var streamsElement) && streamsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamsElement.EnumerateArray())
                {
                    if (stream.ValueKind != JsonValueKind.Object || !stream.TryGetProperty("codec_type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var codecType = typeProp.GetString();

                    if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        if (stream.TryGetProperty("duration", out var streamDurProp) &&
                            TryParseFfprobeDurationSeconds(streamDurProp, out var streamDuration) &&
                            streamDuration > maxStreamDurationSeconds)
                        {
                            maxStreamDurationSeconds = streamDuration;
                        }
                    }

                    if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsAttachedPictureStream(stream) &&
                            string.IsNullOrEmpty(info.VideoCodec) &&
                            stream.TryGetProperty("codec_name", out var vCodec) &&
                            vCodec.ValueKind == JsonValueKind.String)
                        {
                            var vc = vCodec.GetString() ?? string.Empty;
                            info.VideoCodec = vc.ToUpperInvariant() switch
                            {
                                "HEVC" => "HEVC / H.265",
                                "H264" => "AVC / H.264",
                                "AV1" => "AV1",
                                "VP9" => "VP9",
                                "MPEG4" => "MPEG-4",
                                _ => vc,
                            };
                        }

                        if (stream.TryGetProperty("width", out var wProp))
                        {
                            if (wProp.ValueKind == JsonValueKind.Number && wProp.TryGetInt32(out var width))
                            {
                                info.Width = width;
                            }
                            else if (wProp.ValueKind == JsonValueKind.String && int.TryParse(wProp.GetString(), out var widthParsed))
                            {
                                info.Width = widthParsed;
                            }
                        }

                        if (stream.TryGetProperty("height", out var hProp))
                        {
                            if (hProp.ValueKind == JsonValueKind.Number && hProp.TryGetInt32(out var height))
                            {
                                info.Height = height;
                            }
                            else if (hProp.ValueKind == JsonValueKind.String && int.TryParse(hProp.GetString(), out var heightParsed))
                            {
                                info.Height = heightParsed;
                            }
                        }

                        if (stream.TryGetProperty("field_order", out var fieldOrderProp) &&
                            fieldOrderProp.ValueKind == JsonValueKind.String)
                        {
                            isInterlaced = IsInterlacedFieldOrder(fieldOrderProp.GetString());
                        }

                        // Check HDR indicators
                        var colorTransfer = stream.TryGetProperty("color_transfer", out var ctProp) && ctProp.ValueKind == JsonValueKind.String ? ctProp.GetString() : string.Empty;

                        var hasDv = false;
                        var hasHdr10Plus = false;
                        if (stream.TryGetProperty("side_data_list", out var sideDataArray) && sideDataArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var sideData in sideDataArray.EnumerateArray())
                            {
                                if (sideData.ValueKind == JsonValueKind.Object && sideData.TryGetProperty("side_data_type", out var sdtProp) && sdtProp.ValueKind == JsonValueKind.String)
                                {
                                    var sdt = sdtProp.GetString() ?? string.Empty;
                                    if (sdt.Contains("DOVI", StringComparison.OrdinalIgnoreCase) || sdt.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasDv = true;
                                    }

                                    if (sdt.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) || sdt.Contains("HDR Dynamic Metadata", StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasHdr10Plus = true;
                                    }
                                }
                            }
                        }

                        var hasHdr10 = false;
                        var hasHlg = false;
                        if (!string.IsNullOrEmpty(colorTransfer))
                        {
                            if (colorTransfer.Contains("smpte2084", StringComparison.OrdinalIgnoreCase))
                            {
                                hasHdr10 = true;
                            }
                            else if (colorTransfer.Contains("arib-std-b67", StringComparison.OrdinalIgnoreCase))
                            {
                                hasHlg = true;
                            }
                        }

                        if (hasDv && hasHdr10Plus)
                        {
                            info.HdrFormat = "Dolby Vision / HDR10+";
                        }
                        else if (hasDv && hasHdr10)
                        {
                            info.HdrFormat = "Dolby Vision / HDR10";
                        }
                        else if (hasDv && hasHlg)
                        {
                            info.HdrFormat = "Dolby Vision / HLG";
                        }
                        else if (hasDv)
                        {
                            info.HdrFormat = "Dolby Vision";
                        }
                        else if (hasHdr10Plus)
                        {
                            info.HdrFormat = "HDR10+";
                        }
                        else if (hasHdr10)
                        {
                            info.HdrFormat = "HDR10";
                        }
                        else if (hasHlg)
                        {
                            info.HdrFormat = "HLG";
                        }
                    }
                    else if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsCommentaryAudioStream(stream))
                        {
                            continue;
                        }

                        var ac = MapFfprobeAudioCodecFromStream(stream);

                        string channelsStr = null;
                        if (stream.TryGetProperty("channels", out var chanProp))
                        {
                            var channels = 0;
                            if (chanProp.ValueKind == JsonValueKind.Number && chanProp.TryGetInt32(out var chanInt))
                            {
                                channels = chanInt;
                            }
                            else if (chanProp.ValueKind == JsonValueKind.String && int.TryParse(chanProp.GetString(), out var chanParsed))
                            {
                                channels = chanParsed;
                            }

                            if (channels > 0)
                            {
                                channelsStr = channels switch
                                {
                                    1 => "1.0",
                                    2 => "2.0",
                                    6 => "5.1",
                                    8 => "7.1",
                                    _ => $"{channels}.0",
                                };
                            }
                        }

                        var incomingScore = GetAudioCodecScore(ac);
                        var currentScore = GetAudioCodecScore(info.AudioCodec);

                        if (string.IsNullOrEmpty(info.AudioCodec) || incomingScore > currentScore)
                        {
                            info.AudioChannels = null;
                            info.AudioSampleRate = 0;
                            info.AudioBitDepth = 0;

                            if (!string.IsNullOrWhiteSpace(ac))
                            {
                                info.AudioCodec = ac;
                            }

                            if (!string.IsNullOrEmpty(channelsStr))
                            {
                                info.AudioChannels = channelsStr;
                            }

                            if (stream.TryGetProperty("sample_rate", out var srProp))
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

                            if (stream.TryGetProperty("bits_per_raw_sample", out var bprsProp))
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
                    }
                    else if (string.Equals(codecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                    {
                        var subLabel = string.Empty;
                        if (stream.TryGetProperty("tags", out var tagsElem) && tagsElem.ValueKind == JsonValueKind.Object)
                        {
                            if (tagsElem.TryGetProperty("language", out var langProp) && langProp.ValueKind == JsonValueKind.String)
                            {
                                subLabel = langProp.GetString();
                            }

                            if (tagsElem.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(titleProp.GetString()))
                            {
                                subLabel = string.IsNullOrEmpty(subLabel) ? titleProp.GetString() : $"{subLabel} ({titleProp.GetString()})";
                            }
                        }

                        if (string.IsNullOrEmpty(subLabel) && stream.TryGetProperty("codec_name", out var sCodec) && sCodec.ValueKind == JsonValueKind.String)
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

            if (info.DurationSeconds <= 0 && maxStreamDurationSeconds > 0)
            {
                info.DurationSeconds = maxStreamDurationSeconds;
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
            else if (info.Height >= 500 || (info.Width >= 700 && info.Height >= 500))
            {
                info.Resolution = "576p";
            }
            else if (info.Width >= 640 || info.Height >= 400)
            {
                info.Resolution = "480p";
            }

            if (isInterlaced && !string.IsNullOrEmpty(info.Resolution))
            {
                info.Resolution = ApplyInterlacedScanTypeLabel(info.Resolution);
            }

            TagLibInspectorProvider.ApplyFilenameHints(info, fileName);
            return info;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyFfprobeFormatTags(MediaContainerInfo info, JsonElement tagsElement)
    {
        if (string.IsNullOrEmpty(info.Title) && TryGetFfprobeTagString(tagsElement, "title", out var title))
        {
            info.Title = title;
        }

        if (string.IsNullOrEmpty(info.Artist))
        {
            if (TryGetFfprobeTagString(tagsElement, "artist", out var artist) ||
                TryGetFfprobeTagString(tagsElement, "album_artist", out artist))
            {
                info.Artist = artist;
            }
        }

        if (string.IsNullOrEmpty(info.Album) && TryGetFfprobeTagString(tagsElement, "album", out var album))
        {
            info.Album = album;
        }

        if (TryGetFfprobeTagString(tagsElement, "track", out var trackRaw))
        {
            ParseFfprobeIndexTag(trackRaw, out var trackNumber, out var trackTotal);
            if (info.Track == 0 && trackNumber > 0)
            {
                info.Track = trackNumber;
            }

            if (info.TrackCount == 0 && trackTotal > 0)
            {
                info.TrackCount = trackTotal;
            }
        }

        if (TryGetFfprobeTagString(tagsElement, "disc", out var discRaw))
        {
            ParseFfprobeIndexTag(discRaw, out var discNumber, out var discTotal);
            if (info.Disc == 0 && discNumber > 0)
            {
                info.Disc = discNumber;
            }

            if (info.DiscCount == 0 && discTotal > 0)
            {
                info.DiscCount = discTotal;
            }
        }
    }

    private static bool TryGetFfprobeTagString(JsonElement tagsElement, string key, out string value)
    {
        value = null;
        if (tagsElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in tagsElement.EnumerateObject())
        {
            if (!property.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var candidate = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            value = candidate.Trim();
            return true;
        }

        return false;
    }

    private static void ParseFfprobeIndexTag(string raw, out int number, out int total)
    {
        number = 0;
        total = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var parts = raw.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumber) &&
            parsedNumber > 0)
        {
            number = parsedNumber;
        }

        if (parts.Length > 1 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTotal) &&
            parsedTotal > 0)
        {
            total = parsedTotal;
        }
    }

    internal static bool IsInterlacedFieldOrder(string fieldOrder)
    {
        if (string.IsNullOrWhiteSpace(fieldOrder))
        {
            return false;
        }

        return fieldOrder.Equals("tt", StringComparison.OrdinalIgnoreCase) ||
            fieldOrder.Equals("bb", StringComparison.OrdinalIgnoreCase) ||
            fieldOrder.Equals("tb", StringComparison.OrdinalIgnoreCase) ||
            fieldOrder.Equals("bt", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ApplyInterlacedScanTypeLabel(string progressiveResolution)
    {
        return progressiveResolution switch
        {
            "4K UHD (2160p)" => "4K UHD (2160i)",
            "1080p" => "1080i",
            "720p" => "720i",
            "576p" => "576i",
            "480p" => "480i",
            _ => progressiveResolution,
        };
    }
    internal static string MapFfprobeFormatName(string formatName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            return formatName ?? string.Empty;
        }

        var tokens = formatName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return formatName;
        }

        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        if (HasFormatToken(tokens, "matroska") || HasFormatToken(tokens, "webm"))
        {
            if (ext == "webm" && HasFormatToken(tokens, "webm"))
            {
                return "WebM";
            }

            if ((ext == "mkv" || ext == "mka" || ext == "mks" || ext == "mk3d") && HasFormatToken(tokens, "matroska"))
            {
                return "Matroska (MKV)";
            }

            if (HasFormatToken(tokens, "webm") && !HasFormatToken(tokens, "matroska"))
            {
                return "WebM";
            }

            return "Matroska (MKV)";
        }

        if (HasFormatToken(tokens, "mov") || HasFormatToken(tokens, "mp4") || HasFormatToken(tokens, "m4a") ||
            HasFormatToken(tokens, "3gp") || HasFormatToken(tokens, "3g2") || HasFormatToken(tokens, "mj2"))
        {
            return ext switch
            {
                "3gp" => "3GP",
                "3g2" => "3G2",
                "mov" => "MP4",
                "m4v" => "MP4",
                "mp4" => "MP4",
                "m4a" => "MP4",
                _ => "MP4",
            };
        }

        if (HasFormatToken(tokens, "avi"))
        {
            return "AVI";
        }

        if (HasFormatToken(tokens, "flac"))
        {
            return "FLAC";
        }

        if (HasFormatToken(tokens, "mp3"))
        {
            return "MP3";
        }

        return tokens.Length == 1 ? tokens[0] : formatName;
    }

    private static bool TryParseFfprobeDurationSeconds(JsonElement durProp, out double durationSeconds)
    {
        durationSeconds = 0;
        if (durProp.ValueKind == JsonValueKind.Number && durProp.TryGetDouble(out var durationSecNum))
        {
            durationSeconds = durationSecNum;
            return true;
        }

        if (durProp.ValueKind == JsonValueKind.String)
        {
            var durationText = durProp.GetString();
            if (string.IsNullOrWhiteSpace(durationText))
            {
                return false;
            }

            if (durationText.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSecStr))
            {
                durationSeconds = durationSecStr;
                return true;
            }
        }

        return false;
    }

    private static bool IsFfprobeDispositionSet(JsonElement stream, string dispositionKey)
    {
        if (!stream.TryGetProperty("disposition", out var disposition) ||
            disposition.ValueKind != JsonValueKind.Object ||
            !disposition.TryGetProperty(dispositionKey, out var flag))
        {
            return false;
        }

        return flag.ValueKind switch
        {
            JsonValueKind.Number when flag.TryGetInt32(out var value) => value != 0,
            JsonValueKind.True => true,
            JsonValueKind.String => int.TryParse(flag.GetString(), out var parsed) && parsed != 0,
            _ => false,
        };
    }

    private static bool IsAttachedPictureStream(JsonElement stream) => IsFfprobeDispositionSet(stream, "attached_pic");

    private static bool IsCommentaryAudioStream(JsonElement stream) => IsFfprobeDispositionSet(stream, "comment");

    private static string MapFfprobeAudioCodecFromStream(JsonElement stream)
    {
        var ac = string.Empty;
        if (stream.TryGetProperty("codec_name", out var aCodec) && aCodec.ValueKind == JsonValueKind.String)
        {
            var rawCodec = aCodec.GetString() ?? string.Empty;
            ac = rawCodec.ToUpperInvariant() switch
            {
                "EAC3" => "E-AC3 / DD+",
                "AC3" => "AC3 / Dolby Digital",
                "TRUEHD" => "Dolby TrueHD",
                "DTS" => "DTS",
                "FLAC" => "FLAC",
                "AAC" => "AAC",
                "MP3" => "MP3",
                "OPUS" => "Opus",
                _ => rawCodec,
            };
        }

        if (stream.TryGetProperty("profile", out var profileProp) &&
            profileProp.ValueKind == JsonValueKind.String)
        {
            ac = RefineFfprobeAudioCodecFromProfile(ac, profileProp.GetString());
        }

        return ac;
    }

    internal static string RefineFfprobeAudioCodecFromProfile(string codecFromName, string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return codecFromName ?? string.Empty;
        }

        if (profile.Contains("DTS:X", StringComparison.OrdinalIgnoreCase) ||
            profile.Contains("DTS-HD MA + DTS:X", StringComparison.OrdinalIgnoreCase))
        {
            return "DTS:X";
        }

        if (profile.Contains("DTS-HD MA", StringComparison.OrdinalIgnoreCase))
        {
            return "DTS-HD MA";
        }

        if (profile.Contains("DTS-HD HRA", StringComparison.OrdinalIgnoreCase))
        {
            return "DTS-HD HRA";
        }

        if (profile.Contains("TrueHD", StringComparison.OrdinalIgnoreCase) &&
            profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
        {
            return "Dolby TrueHD / Atmos";
        }

        if (profile.Contains("Dolby Digital Plus", StringComparison.OrdinalIgnoreCase) &&
            profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
        {
            return "Dolby Atmos";
        }

        if (profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(codecFromName) &&
            codecFromName.Contains("TrueHD", StringComparison.OrdinalIgnoreCase))
        {
            return "Dolby TrueHD / Atmos";
        }

        return codecFromName ?? string.Empty;
    }

    private static bool HasFormatToken(string[] tokens, string token)
    {
        foreach (var entry in tokens)
        {
            if (entry.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetAudioCodecScore(string codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return 0;
        }

        if (codec.Contains("TrueHD", StringComparison.OrdinalIgnoreCase) || codec.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        if (codec.Contains("DTS:X", StringComparison.OrdinalIgnoreCase))
        {
            return 46;
        }

        if (codec.Contains("DTS-HD", StringComparison.OrdinalIgnoreCase))
        {
            return 45;
        }

        if (codec.Contains("FLAC", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("ALAC", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("Apple Lossless", StringComparison.OrdinalIgnoreCase))
        {
            return 35;
        }

        if (codec.Contains("E-AC3", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("Dolby Digital Plus", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("DD+", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("EAC3", StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }

        if (codec.Contains("DTS", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if (codec.Contains("AC3", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("Dolby Digital", StringComparison.OrdinalIgnoreCase))
        {
            return 15;
        }

        if (codec.Contains("Opus", StringComparison.OrdinalIgnoreCase))
        {
            return 12;
        }

        if (codec.Contains("AAC", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (codec.Contains("Vorbis", StringComparison.OrdinalIgnoreCase))
        {
            return 8;
        }

        if (codec.Contains("MP3", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("MPEG", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("PCM", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return 1;
    }

    private async Task<string> ProbeBinaryVersionAsync(string binary, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binary,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-version");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var probeTimeout = TimeSpan.FromSeconds(10);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(probeTimeout);

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token)).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            var stdout = (await stdoutTask.ConfigureAwait(false))?.Trim();
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                return ExtractFirstLine(stdout);
            }

            var stderr = (await stderrTask.ConfigureAwait(false))?.Trim();
            return string.IsNullOrWhiteSpace(stderr) ? null : ExtractFirstLine(stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Failed to kill FFprobe process after health probe timeout");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "FFprobe health probe failed for {0}", binary);
            return null;
        }
    }

    private static string ExtractFirstLine(string output)
    {
        var newlineIndex = output.IndexOf('\n');
        return newlineIndex >= 0 ? output[..newlineIndex].Trim() : output.Trim();
    }

    private string FindBinary()
    {
        if (!string.IsNullOrWhiteSpace(this.customBinaryPath))
        {
            return this.customBinaryPath;
        }

        return CliProcessDiscovery.FindExecutable("ffprobe", "FFPROBE_PATH", DefaultSearchPaths);
    }
}
