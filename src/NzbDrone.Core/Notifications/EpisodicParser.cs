// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Notifications;

public class EpisodicParser : IEpisodicParser
{
    private static readonly Regex SeasonEpisodeRangeRegex = new(
        @"(?i)\bS(?<season>\d{1,2})E(?<epStart>\d{1,3})(?:-(?:(?<epTail>(?:E|EP)\d{1,3}(?:-(?:E|EP)\d{1,3})*)|(?<epEndCompact>\d{1,2}))|(?<extraEps>(?:[-._,]?[eE]\d{1,3})+))?\b",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex SeasonEpisodeAltRangeRegex = new(
        @"(?i)\b(?<season>\d{1,2})x(?<epStart>\d{1,3})(?:-(?:(?<epTail>(?:\d{1,2})x\d{1,3}(?:-(?:\d{1,2})x\d{1,3})*)|(?<epEndCompact>\d{1,2}))|(?<extraEps>(?:x\d{1,3})+))?\b",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex EpTailEpisodeRegex = new(
        @"(?i)(?:E|EP)(\d{1,3})",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex EpTailAltEpisodeRegex = new(
        @"(?i)x(\d{1,3})",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex SpecialEpisodeRegex = new(
        @"(?i)\b(?:SP|Special)[.\s_-]*(?<ep>\d{1,3})\b",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    public (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle) ExtractEpisodicInfo(string name)
    {
        return ExtractEpisodicInfoStatic(name);
    }

    public (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle, List<int> EpisodeNumbers, string FormattedRange) ExtractDetailedEpisodicInfo(string name)
    {
        return ExtractDetailedEpisodicInfoStatic(name);
    }

    public List<int> ExtractEpisodeNumbers(string name)
    {
        return ExtractEpisodeNumbersStatic(name);
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
        var detailed = ExtractDetailedEpisodicInfoStatic(name);
        return (detailed.SeasonNumber, detailed.EpisodeNumber, detailed.EpisodeTitle);
    }

    public static List<int> ExtractEpisodeNumbersStatic(string name)
    {
        return ExtractDetailedEpisodicInfoStatic(name).EpisodeNumbers;
    }

    public static (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle, List<int> EpisodeNumbers, string FormattedRange) ExtractDetailedEpisodicInfoStatic(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null, null, new List<int>(), null);
        }

        if (TryExtractFromSeasonEpisodeMatches(SeasonEpisodeRangeRegex.Matches(name), out var seasonResult, out var epsResult))
        {
            var formatted = FormatEpisodeRange(seasonResult, epsResult);
            return (seasonResult, epsResult[0], null, epsResult, formatted);
        }

        if (TryExtractFromSeasonEpisodeMatches(SeasonEpisodeAltRangeRegex.Matches(name), out var seasonAlt, out var epsAlt))
        {
            var formatted = FormatEpisodeRange(seasonAlt, epsAlt);
            return (seasonAlt, epsAlt[0], null, epsAlt, formatted);
        }

        var matchSp = SpecialEpisodeRegex.Match(name);
        if (matchSp.Success && int.TryParse(matchSp.Groups["ep"].Value, out var epSp))
        {
            var eps = new List<int> { epSp };
            var formatted = FormatEpisodeRange(0, eps);
            return (0, epSp, null, eps, formatted);
        }

        return (null, null, null, new List<int>(), null);
    }

    private static bool TryExtractFromSeasonEpisodeMatches(MatchCollection matches, out int season, out List<int> episodes)
    {
        season = 0;
        episodes = new List<int>();
        if (matches.Count == 0)
        {
            return false;
        }

        int? firstSeason = null;
        foreach (Match match in matches)
        {
            if (!match.Success ||
                !int.TryParse(match.Groups["season"].Value, out var s) ||
                !int.TryParse(match.Groups["epStart"].Value, out var epStart))
            {
                continue;
            }

            if (firstSeason == null)
            {
                firstSeason = s;
            }
            else if (firstSeason != s)
            {
                continue;
            }

            episodes.AddRange(BuildEpisodeListFromMatch(match, epStart));
        }

        if (firstSeason == null || episodes.Count == 0)
        {
            return false;
        }

        season = firstSeason.Value;
        episodes = episodes.Distinct().OrderBy(x => x).ToList();
        return true;
    }

    private static List<int> BuildEpisodeListFromMatch(Match match, int epStart)
    {
        var eps = new List<int> { epStart };
        var epTailGroup = match.Groups["epTail"];
        var extraEpsGroup = match.Groups["extraEps"];

        if (epTailGroup.Success)
        {
            var tailExtractor = Regex.IsMatch(epTailGroup.Value, @"(?i)^(?:E|EP)\d", RegexOptions.None, TimeSpan.FromMilliseconds(250))
                ? EpTailEpisodeRegex
                : EpTailAltEpisodeRegex;
            return BuildEpisodeListFromEpTail(epStart, epTailGroup.Value, tailExtractor);
        }

        if (TryApplyEpisodeEnd(match.Groups["epEndCompact"], epStart, eps, out var expandedEps))
        {
            return expandedEps;
        }

        if (extraEpsGroup.Success)
        {
            var extraMatches = Regex.Matches(extraEpsGroup.Value, @"\d{1,3}", RegexOptions.None, TimeSpan.FromSeconds(2));
            foreach (Match m in extraMatches)
            {
                if (int.TryParse(m.Value, out var eNum))
                {
                    eps.Add(eNum);
                }
            }
        }

        return eps;
    }

    private static List<int> BuildEpisodeListFromEpTail(int epStart, string epTail, Regex tailEpisodeRegex)
    {
        var tailEpisodes = new List<int>();
        foreach (Match m in tailEpisodeRegex.Matches(epTail))
        {
            if (int.TryParse(m.Groups[1].Value, out var ep))
            {
                tailEpisodes.Add(ep);
            }
        }

        if (tailEpisodes.Count >= 2)
        {
            var listed = new List<int> { epStart };
            listed.AddRange(tailEpisodes);
            return listed;
        }

        if (tailEpisodes.Count == 1)
        {
            return ExpandEpisodeEnd(epStart, tailEpisodes[0], new List<int> { epStart }, allowNonContiguousEnd: true);
        }

        return new List<int> { epStart };
    }

    private static bool TryApplyEpisodeEnd(
        Group epEndCompactGroup,
        int epStart,
        List<int> eps,
        out List<int> expandedEps)
    {
        expandedEps = null;
        if (epEndCompactGroup.Success && int.TryParse(epEndCompactGroup.Value, out var epEndCompact))
        {
            expandedEps = ExpandEpisodeEnd(epStart, epEndCompact, eps, allowNonContiguousEnd: true);
            return true;
        }

        return false;
    }

    private static List<int> ExpandEpisodeEnd(int epStart, int epEnd, List<int> eps, bool allowNonContiguousEnd)
    {
        if (epEnd >= epStart && epEnd - epStart <= 50)
        {
            return Enumerable.Range(epStart, epEnd - epStart + 1).ToList();
        }

        if (allowNonContiguousEnd && epEnd != epStart)
        {
            var result = new List<int>(eps) { epEnd };
            return result;
        }

        return new List<int>(eps);
    }

    private static string FormatEpisodeRange(int season, List<int> episodes)
    {
        if (episodes == null || episodes.Count == 0)
        {
            return null;
        }

        if (episodes.Count == 1)
        {
            return $"S{season:D2}E{episodes[0]:D2}";
        }

        var isContiguous = true;
        for (var i = 1; i < episodes.Count; i++)
        {
            if (episodes[i] != episodes[i - 1] + 1)
            {
                isContiguous = false;
                break;
            }
        }

        if (isContiguous)
        {
            return $"S{season:D2}E{episodes.First():D2}-E{episodes.Last():D2}";
        }

        return $"S{season:D2}" + string.Join(string.Empty, episodes.Select(e => $"E{e:D2}"));
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
            if (c is '_' or '*' or '[' or ']' or '~' or '>' or '|' or '\\' or '`')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    public static string FormatEtaStatic(long seconds)
    {
        if (seconds <= 0)
        {
            return "00:00:00";
        }

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 24
            ? $"{(int)ts.TotalDays}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
