// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Subtitles;

public class SubtitleConversionService : ISubtitleConversionService
{
    private readonly ISubtitleEncodingDetector _encodingDetector;

    private static readonly Regex TimecodeLineRegex = new(
        @"^\s*(?:(?<sh>\d{1,2}):)?(?<sm>\d{1,2}):(?<ss>\d{2})[,.](?<sms>\d{1,3})\s*-->\s*(?:(?<eh>\d{1,2}):)?(?<em>\d{1,2}):(?<es>\d{2})[,.](?<ems>\d{1,3})(?<settings>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex MicroDvdRegex = new(
        @"^\s*\{(?<start>\d+)\}\{(?<end>\d+)\}(?<text>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex FontTagRegex = new(
        @"</?font[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AssTagRegex = new(
        @"\{[^}]+\}",
        RegexOptions.Compiled);

    private static readonly Regex ValidVttSettingRegex = new(
        @"^(?:line|position|size|align|vertical):",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SubtitleConversionService(ISubtitleEncodingDetector encodingDetector = null)
    {
        _encodingDetector = encodingDetector ?? new SubtitleEncodingDetector();
    }

    public string ConvertToWebVtt(byte[] rawBytes, string format = "srt")
    {
        if (rawBytes == null || rawBytes.Length == 0)
        {
            return "WEBVTT\n\n";
        }

        var decoded = _encodingDetector.DecodeToUtf8(rawBytes);
        return ConvertToWebVtt(decoded, format);
    }

    public string ConvertToWebVtt(string content, string format = "srt")
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "WEBVTT\n\n";
        }

        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeExistingWebVtt(trimmed);
        }

        var fmt = (format ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (fmt == "sub")
        {
            if (MicroDvdRegex.IsMatch(trimmed))
            {
                return ConvertMicroDvdToWebVtt(trimmed);
            }
        }

        return ConvertSrtToWebVtt(content);
    }

    public string ConvertSrtToWebVtt(string srtContent)
    {
        if (string.IsNullOrWhiteSpace(srtContent))
        {
            return "WEBVTT\n\n";
        }

        var normalizedLines = srtContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        sb.Append("WEBVTT\n\n");

        var cueIndex = 1;
        var i = 0;
        var lineCount = normalizedLines.Length;

        while (i < lineCount)
        {
            var line = normalizedLines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                i++;
                continue;
            }

            // Skip WebVTT header if present
            if (line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            string timecodeLine = null;
            if (int.TryParse(line, out _))
            {
                // Next non-empty line should be the timecode
                var j = i + 1;
                while (j < lineCount && string.IsNullOrWhiteSpace(normalizedLines[j]))
                {
                    j++;
                }

                if (j < lineCount && TimecodeLineRegex.IsMatch(normalizedLines[j]))
                {
                    timecodeLine = normalizedLines[j];
                    i = j + 1;
                }
            }

            // If not found yet, check if the line itself is a timecode
            if (timecodeLine == null && TimecodeLineRegex.IsMatch(line))
            {
                timecodeLine = line;
                i++;
            }

            if (timecodeLine == null)
            {
                i++;
                continue;
            }

            var timecodeMatch = TimecodeLineRegex.Match(timecodeLine);
            var normalizedTimecode = NormalizeTimecode(timecodeMatch);

            // Collect text lines for this cue
            var textLines = new List<string>();
            while (i < lineCount)
            {
                var textLine = normalizedLines[i];
                if (string.IsNullOrWhiteSpace(textLine))
                {
                    break;
                }

                // Subsequent cue without blank line separator
                if (int.TryParse(textLine.Trim(), out _) && i + 1 < lineCount && TimecodeLineRegex.IsMatch(normalizedLines[i + 1]))
                {
                    break;
                }

                // Or direct timecode on next line
                if (TimecodeLineRegex.IsMatch(textLine))
                {
                    break;
                }

                var cleaned = CleanCueText(textLine);
                if (!string.IsNullOrEmpty(cleaned))
                {
                    textLines.Add(cleaned);
                }

                i++;
            }

            if (textLines.Count > 0)
            {
                sb.Append(cueIndex).Append('\n');
                sb.Append(normalizedTimecode).Append('\n');
                foreach (var tl in textLines)
                {
                    sb.Append(tl).Append('\n');
                }

                sb.Append('\n');
                cueIndex++;
            }
        }

        return sb.ToString();
    }

    private static string NormalizeExistingWebVtt(string vtt)
    {
        var lines = vtt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        sb.Append("WEBVTT\n\n");

        var i = 0;

        // Skip any existing WEBVTT line and its header attributes
        if (lines.Length > 0 && lines[0].Trim().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
        {
            i = 1;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                // Include comments/headers e.g. NOTE
                var headerLine = lines[i].Trim();
                if (headerLine.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
                    headerLine.StartsWith("STYLE", StringComparison.OrdinalIgnoreCase) ||
                    headerLine.StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(headerLine).Append('\n');
                }

                i++;
            }

            if (sb.Length > "WEBVTT\n\n".Length)
            {
                sb.Append('\n');
            }
        }

        // Output remaining cues
        var remaining = string.Join("\n", lines, i, lines.Length - i).Trim();
        if (!string.IsNullOrEmpty(remaining))
        {
            sb.Append(remaining).Append('\n');
        }

        return sb.ToString();
    }

    private static string ConvertMicroDvdToWebVtt(string subContent, double fps = 23.976)
    {
        var lines = subContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        sb.Append("WEBVTT\n\n");

        var cueIndex = 1;

        foreach (var rawLine in lines)
        {
            var match = MicroDvdRegex.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            if (!long.TryParse(match.Groups["start"].Value, out var startFrame) ||
                !long.TryParse(match.Groups["end"].Value, out var endFrame))
            {
                continue;
            }

            // MicroDVD FPS header definition, e.g. {1}{1}25.000
            if (startFrame == 1 && endFrame == 1)
            {
                var fpsStr = match.Groups["text"].Value.Trim();
                if (double.TryParse(fpsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var customFps) && customFps > 0)
                {
                    fps = customFps;
                    continue;
                }
            }

            var startSec = startFrame / fps;
            var endSec = endFrame / fps;

            var startTime = TimeSpan.FromSeconds(startSec);
            var endTime = TimeSpan.FromSeconds(endSec);

            var timecodeStr = $"{FormatTimeSpan(startTime)} --> {FormatTimeSpan(endTime)}";
            var text = match.Groups["text"].Value.Replace("|", "\n");
            text = CleanCueText(text);

            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.Append(cueIndex).Append('\n');
                sb.Append(timecodeStr).Append('\n');
                sb.Append(text).Append("\n\n");
                cueIndex++;
            }
        }

        return sb.ToString();
    }

    private static string NormalizeTimecode(Match m)
    {
        var sh = m.Groups["sh"].Success ? int.Parse(m.Groups["sh"].Value, CultureInfo.InvariantCulture) : 0;
        var sm = int.Parse(m.Groups["sm"].Value, CultureInfo.InvariantCulture);
        var ss = int.Parse(m.Groups["ss"].Value, CultureInfo.InvariantCulture);
        var sms = m.Groups["sms"].Value.PadRight(3, '0');
        if (sms.Length > 3)
        {
            sms = sms.Substring(0, 3);
        }

        var eh = m.Groups["eh"].Success ? int.Parse(m.Groups["eh"].Value, CultureInfo.InvariantCulture) : 0;
        var em = int.Parse(m.Groups["em"].Value, CultureInfo.InvariantCulture);
        var es = int.Parse(m.Groups["es"].Value, CultureInfo.InvariantCulture);
        var ems = m.Groups["ems"].Value.PadRight(3, '0');
        if (ems.Length > 3)
        {
            ems = ems.Substring(0, 3);
        }

        var start = $"{sh:D2}:{sm:D2}:{ss:D2}.{sms}";
        var end = $"{eh:D2}:{em:D2}:{es:D2}.{ems}";

        var rawSettings = m.Groups["settings"].Value.Trim();
        var validSettings = CleanSettings(rawSettings);

        return string.IsNullOrEmpty(validSettings)
            ? $"{start} --> {end}"
            : $"{start} --> {end} {validSettings}";
    }

    private static string CleanSettings(string rawSettings)
    {
        if (string.IsNullOrWhiteSpace(rawSettings))
        {
            return string.Empty;
        }

        var parts = rawSettings.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var validParts = new List<string>();

        foreach (var part in parts)
        {
            if (ValidVttSettingRegex.IsMatch(part))
            {
                validParts.Add(part);
            }
        }

        return string.Join(" ", validParts);
    }

    private static string CleanCueText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Strip HTML <font> tags
        var cleaned = FontTagRegex.Replace(text, string.Empty);

        // Strip ASS override tags like {\an8} or {y:i}
        cleaned = AssTagRegex.Replace(cleaned, string.Empty);

        return cleaned.Trim();
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        var hours = (int)ts.TotalHours;
        return $"{hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
