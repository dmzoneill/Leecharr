// Copyright (c) PlaceholderCompany. All rights reserved.

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

public class MediaInfoInspectorProvider : IMediaInspectorProvider
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly TagLibInspectorProvider fallbackProvider = new();
    private readonly string customBinaryPath;
    private readonly TimeSpan executionTimeout;

    public MediaInfoInspectorProvider()
        : this(null, TimeSpan.FromSeconds(60))
    {
    }

    internal MediaInfoInspectorProvider(string customBinaryPath, TimeSpan? executionTimeout = null)
    {
        this.customBinaryPath = customBinaryPath;
        this.executionTimeout = executionTimeout ?? TimeSpan.FromSeconds(60);
    }

    public string ProviderId => "MediaInfo";

    public string DisplayName => "MediaInfo (CLI / Shared Library)";

    public string Version => "24.06 (MediaInfo CLI)";

    public string Description => "Industry standard MediaInfo binary inspector providing deep container and stream analysis.";

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
        SupportsVideoThumbnails = false,
        SupportsPureManagedStreams = false,
    };

    public Task<MediaInspectorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        var binary = this.FindBinary();
        if (binary != null)
        {
            return Task.FromResult(new MediaInspectorHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"MediaInfo CLI executable found at {binary}.",
                DependencyChecks = new List<string> { $"MediaInfo binary: {binary}" },
            });
        }

        return Task.FromResult(new MediaInspectorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "MediaInfo executable not found on PATH or standard locations.",
            Warnings = new List<string> { "Install mediainfo or set MEDIAINFO_PATH environment variable." },
        });
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
            startInfo.ArgumentList.Add("--Output=JSON");
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
                catch
                {
                    // Suppress process kill errors
                }

                Logger.Warn("MediaInfo execution timed out after {0} seconds for {1}", this.executionTimeout.TotalSeconds, mediaPath);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stderr) && process.ExitCode != 0)
            {
                Logger.Warn("MediaInfo reported errors on stderr: {0}", stderr);
            }

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var parsed = ParseMediaInfoJson(stdout, Path.GetFileName(mediaPath));
                if (parsed != null)
                {
                    return parsed;
                }
            }

            return this.fallbackProvider.InspectFile(mediaPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "MediaInfo failed to inspect media: {0}", mediaPath);
            return this.fallbackProvider.InspectFile(mediaPath);
        }
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        return this.InspectMediaAsync(filePath).GetAwaiter().GetResult();
    }

    public MediaContainerInfo Inspect(Stream stream, string fileName = "")
    {
        return this.fallbackProvider.Inspect(stream, fileName);
    }

    public static MediaContainerInfo ParseMediaInfoJson(string json, string fileName = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("media", out var mediaElement) ||
                !mediaElement.TryGetProperty("track", out var trackArray) ||
                trackArray.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var info = new MediaContainerInfo();

            foreach (var track in trackArray.EnumerateArray())
            {
                if (!track.TryGetProperty("@type", out var typeProp))
                {
                    continue;
                }

                var trackType = typeProp.GetString();

                if (string.Equals(trackType, "General", StringComparison.OrdinalIgnoreCase))
                {
                    if (track.TryGetProperty("Format", out var fmt))
                    {
                        info.ContainerFormat = fmt.GetString();
                    }

                    if (track.TryGetProperty("Duration", out var dur) &&
                        TryGetDouble(dur, out var durationSec))
                    {
                        info.DurationSeconds = durationSec;
                    }
                }
                else if (string.Equals(trackType, "Video", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(info.VideoCodec) && track.TryGetProperty("Format", out var vFmt))
                    {
                        info.VideoCodec = vFmt.GetString();
                    }

                    if (track.TryGetProperty("Width", out var wProp) && TryGetInt(wProp, out var width))
                    {
                        info.Width = width;
                    }

                    if (track.TryGetProperty("Height", out var hProp) && TryGetInt(hProp, out var height))
                    {
                        info.Height = height;
                    }

                    // Check HDR format
                    var hdrString = string.Empty;
                    if (track.TryGetProperty("HDR_Format_Commercial", out var hdrComm))
                    {
                        hdrString += " " + hdrComm.GetString();
                    }

                    if (track.TryGetProperty("HDR_Format", out var hdrFmt))
                    {
                        hdrString += " " + hdrFmt.GetString();
                    }

                    if (track.TryGetProperty("HDR_Format_Compatibility", out var hdrComp))
                    {
                        hdrString += " " + hdrComp.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(hdrString))
                    {
                        var hdrUpper = hdrString.ToUpperInvariant();
                        if (hdrUpper.Contains("DOLBY VISION") || hdrUpper.Contains("DV"))
                        {
                            info.HdrFormat = "Dolby Vision";
                        }
                        else if (hdrUpper.Contains("HDR10+"))
                        {
                            info.HdrFormat = "HDR10+";
                        }
                        else if (hdrUpper.Contains("HDR10") || hdrUpper.Contains("SMPTE ST 2086"))
                        {
                            info.HdrFormat = "HDR10";
                        }
                        else if (hdrUpper.Contains("HLG"))
                        {
                            info.HdrFormat = "HLG";
                        }
                    }
                }
                else if (string.Equals(trackType, "Audio", StringComparison.OrdinalIgnoreCase))
                {
                    var audioFmt = string.Empty;
                    if (track.TryGetProperty("Format_Commercial_IfAny", out var commFmt))
                    {
                        audioFmt = commFmt.GetString();
                    }
                    else if (track.TryGetProperty("Format", out var aFmt))
                    {
                        audioFmt = aFmt.GetString();
                    }

                    string channelsStr = null;
                    if (track.TryGetProperty("Channels", out var chanProp) &&
                        TryGetInt(chanProp, out var channels))
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

                    var incomingScore = GetAudioCodecScore(audioFmt);
                    var currentScore = GetAudioCodecScore(info.AudioCodec);

                    if (string.IsNullOrEmpty(info.AudioCodec) || incomingScore > currentScore)
                    {
                        if (!string.IsNullOrWhiteSpace(audioFmt))
                        {
                            info.AudioCodec = audioFmt;
                        }

                        if (!string.IsNullOrEmpty(channelsStr))
                        {
                            info.AudioChannels = channelsStr;
                        }

                        if (track.TryGetProperty("SamplingRate", out var srProp) &&
                            TryGetInt(srProp, out var sampleRate))
                        {
                            info.AudioSampleRate = sampleRate;
                        }

                        if (track.TryGetProperty("BitDepth", out var bdProp) &&
                            TryGetInt(bdProp, out var bitDepth))
                        {
                            info.AudioBitDepth = bitDepth;
                        }
                    }
                }
                else if (string.Equals(trackType, "Text", StringComparison.OrdinalIgnoreCase))
                {
                    var subLabel = string.Empty;
                    if (track.TryGetProperty("Language", out var langProp))
                    {
                        subLabel = langProp.GetString();
                    }

                    if (track.TryGetProperty("Title", out var titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
                    {
                        subLabel = string.IsNullOrEmpty(subLabel) ? titleProp.GetString() : $"{subLabel} ({titleProp.GetString()})";
                    }

                    if (string.IsNullOrEmpty(subLabel) && track.TryGetProperty("Format", out var textFmt))
                    {
                        subLabel = textFmt.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(subLabel))
                    {
                        info.SubtitleTracks.Add(subLabel);
                    }
                }
            }

            // Derive resolution string
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
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to parse MediaInfo JSON output: {0}", ex.Message);
            return null;
        }
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
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

    private string FindBinary()
    {
        if (!string.IsNullOrWhiteSpace(this.customBinaryPath))
        {
            return this.customBinaryPath;
        }

        return CliProcessDiscovery.FindExecutable("mediainfo", "MEDIAINFO_PATH", new[] { "/usr/bin/mediainfo", "/usr/local/bin/mediainfo" });
    }
}
