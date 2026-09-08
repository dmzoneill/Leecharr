// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Notifications;

public class EpisodicParser : IEpisodicParser
{
    private static readonly Regex SeasonEpisodeRegex = new(@"\bS(\d{1,2})E(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonEpisodeAltRegex = new(@"\b(\d{1,2})x(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle) ExtractEpisodicInfo(string name)
    {
        return ExtractEpisodicInfoStatic(name);
    }

    public (string ContainerFormat, string Resolution, string VideoCodec, string HdrFormat, string AudioCodec, string AudioChannels, string AudioLanguage, List<string> SubtitleLanguages) ExtractStreamSpecs(string mediaInfoJson)
    {
        return ExtractStreamSpecsStatic(mediaInfoJson);
    }

    public string EscapeMarkdown(string text)
    {
        return EscapeMarkdownStatic(text);
    }

    public string FormatEta(long seconds)
    {
        return FormatEtaStatic(seconds);
    }

    public static (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle) ExtractEpisodicInfoStatic(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null, null);
        }

        var match = SeasonEpisodeRegex.Match(name);
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out var s) &&
            int.TryParse(match.Groups[2].Value, out var e))
        {
            return (s, e, null);
        }

        var matchAlt = SeasonEpisodeAltRegex.Match(name);
        if (matchAlt.Success &&
            int.TryParse(matchAlt.Groups[1].Value, out var sAlt) &&
            int.TryParse(matchAlt.Groups[2].Value, out var eAlt))
        {
            return (sAlt, eAlt, null);
        }

        return (null, null, null);
    }

    public static (string ContainerFormat, string Resolution, string VideoCodec, string HdrFormat, string AudioCodec, string AudioChannels, string AudioLanguage, List<string> SubtitleLanguages) ExtractStreamSpecsStatic(string mediaInfoJson)
    {
        if (string.IsNullOrWhiteSpace(mediaInfoJson))
        {
            return (null, null, null, null, null, null, null, new List<string>());
        }

        try
        {
            using var doc = JsonDocument.Parse(mediaInfoJson);
            var root = doc.RootElement;
            var container = root.TryGetProperty("ContainerFormat", out var c) ? c.GetString() : null;
            var resolution = root.TryGetProperty("Resolution", out var r) ? r.GetString() : null;
            var videoCodec = root.TryGetProperty("VideoCodec", out var v) ? v.GetString() : null;
            var hdr = root.TryGetProperty("HdrFormat", out var h) ? h.GetString() : null;
            var audioCodec = root.TryGetProperty("AudioCodec", out var a) ? a.GetString() : null;
            var audioChannels = root.TryGetProperty("AudioChannels", out var ac) ? ac.GetString() : null;
            var audioLanguage = root.TryGetProperty("AudioLanguage", out var al) ? al.GetString() : null;
            var subtitleLanguages = new List<string>();

            if (root.TryGetProperty("SubtitleTracks", out var st) && st.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in st.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        subtitleLanguages.Add(s);
                    }
                }
            }

            return (container, resolution, videoCodec, hdr, audioCodec, audioChannels, audioLanguage, subtitleLanguages);
        }
        catch
        {
            return (null, null, null, null, null, null, null, new List<string>());
        }
    }

    public static string EscapeMarkdownStatic(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if (c is '_' or '*' or '[' or ']' or '(' or ')' or '~' or '>' or '|' or '\\' or '`')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    public static string FormatEtaStatic(long seconds)
    {
        if (seconds <= 0 || seconds >= 8640000)
        {
            return "00:00:00";
        }

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 24
            ? $"{(int)ts.TotalDays}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
