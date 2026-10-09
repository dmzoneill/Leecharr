// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Organizer;

public class FileNameBuilder : IFileNameBuilder
{
    private static readonly Regex TokenRegex = new(
        @"\{(?<token>[a-zA-Z0-9_\-\. ]+?)(?::(?<format>[a-zA-Z0-9_\-]+))?\}",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex EmptyBracketRegex = new(
        @"\[\s*[-_.]*\s*\]|\(\s*[-_.]*\s*\)|\{\s*[-_.]*\s*\}",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex MultipleSpacesRegex = new(
        @"\s+",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex RepeatedDashesRegex = new(
        @"\s*-\s*(?:-\s*)+",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private readonly IFileNameSanitizer fileNameSanitizer;
    private readonly IPathTruncator pathTruncator;

    public FileNameBuilder()
        : this(new FileNameSanitizer(), new PathTruncator())
    {
    }

    public FileNameBuilder(IFileNameSanitizer fileNameSanitizer, IPathTruncator pathTruncator)
    {
        this.fileNameSanitizer = fileNameSanitizer ?? new FileNameSanitizer();
        this.pathTruncator = pathTruncator ?? new PathTruncator();
    }

    public string BuildFileName(EpisodeNamingContext context, string pattern = null, NamingConfig namingConfig = null)
    {
        if (context == null)
        {
            return string.Empty;
        }

        var config = namingConfig ?? new NamingConfig();
        var template = !string.IsNullOrWhiteSpace(pattern)
            ? pattern
            : (context.AbsoluteEpisodeNumbers.Count > 0 && context.EpisodeNumbers.Count == 0
                ? config.AnimeEpisodeFormat
                : (context.AirDate.HasValue && context.EpisodeNumbers.Count == 0
                    ? config.DailyEpisodeFormat
                    : config.StandardEpisodeFormat));

        var replaced = ReplaceTokens(template, token => ResolveEpisodeToken(token, context, config, this.fileNameSanitizer), config);
        var cleaned = CleanBuiltString(replaced);

        var extension = !string.IsNullOrWhiteSpace(context.Extension)
            ? (context.Extension.StartsWith('.') ? context.Extension : "." + context.Extension)
            : string.Empty;

        var fullFileName = cleaned + extension;
        if (config.ReplaceIllegalCharacters)
        {
            fullFileName = this.fileNameSanitizer.SanitizeFileName(
                fullFileName,
                config.ColonReplacementFormat,
                config.CustomColonReplacementFormat);
        }
        else
        {
            fullFileName = ApplyBaselineSafetyForFile(fullFileName);
        }

        return this.pathTruncator.TruncateFileName(fullFileName);
    }

    public string BuildFileName(MovieNamingContext context, string pattern = null, NamingConfig namingConfig = null)
    {
        if (context == null)
        {
            return string.Empty;
        }

        var config = namingConfig ?? new NamingConfig();
        var template = !string.IsNullOrWhiteSpace(pattern)
            ? pattern
            : config.StandardMovieFormat;

        var replaced = ReplaceTokens(template, token => ResolveMovieToken(token, context, config, this.fileNameSanitizer), config);
        var cleaned = CleanBuiltString(replaced);

        var extension = !string.IsNullOrWhiteSpace(context.Extension)
            ? (context.Extension.StartsWith('.') ? context.Extension : "." + context.Extension)
            : string.Empty;

        var fullFileName = cleaned + extension;
        if (config.ReplaceIllegalCharacters)
        {
            fullFileName = this.fileNameSanitizer.SanitizeFileName(
                fullFileName,
                config.ColonReplacementFormat,
                config.CustomColonReplacementFormat);
        }
        else
        {
            fullFileName = ApplyBaselineSafetyForFile(fullFileName);
        }

        return this.pathTruncator.TruncateFileName(fullFileName);
    }

    public string BuildSeriesDirectory(EpisodeNamingContext context, string pattern = null, NamingConfig namingConfig = null)
    {
        if (context == null)
        {
            return string.Empty;
        }

        var config = namingConfig ?? new NamingConfig();
        var template = !string.IsNullOrWhiteSpace(pattern) ? pattern : config.SeriesFolderFormat;
        var replaced = ReplaceTokens(template, token => ResolveEpisodeToken(token, context, config, this.fileNameSanitizer), config);
        var cleaned = CleanBuiltString(replaced);

        if (config.ReplaceIllegalCharacters)
        {
            cleaned = this.fileNameSanitizer.SanitizeFolderName(
                cleaned,
                config.ColonReplacementFormat,
                config.CustomColonReplacementFormat);
        }
        else
        {
            cleaned = ApplyBaselineSafetyForFolder(cleaned);
        }

        return this.pathTruncator.TruncateFolderName(cleaned);
    }

    public string BuildSeasonDirectory(EpisodeNamingContext context, string pattern = null, NamingConfig namingConfig = null)
    {
        if (context == null)
        {
            return string.Empty;
        }

        var config = namingConfig ?? new NamingConfig();
        if (context.SeasonNumber == 0)
        {
            return "Specials";
        }

        var template = !string.IsNullOrWhiteSpace(pattern) ? pattern : config.SeasonFolderFormat;
        var replaced = ReplaceTokens(template, token => ResolveEpisodeToken(token, context, config, this.fileNameSanitizer), config);
        var cleaned = CleanBuiltString(replaced);

        if (config.ReplaceIllegalCharacters)
        {
            cleaned = this.fileNameSanitizer.SanitizeFolderName(
                cleaned,
                config.ColonReplacementFormat,
                config.CustomColonReplacementFormat);
        }
        else
        {
            cleaned = ApplyBaselineSafetyForFolder(cleaned);
        }

        return this.pathTruncator.TruncateFolderName(cleaned);
    }

    public string BuildMovieDirectory(MovieNamingContext context, string pattern = null, NamingConfig namingConfig = null)
    {
        if (context == null)
        {
            return string.Empty;
        }

        var config = namingConfig ?? new NamingConfig();
        var template = !string.IsNullOrWhiteSpace(pattern) ? pattern : config.MovieFolderFormat;
        var replaced = ReplaceTokens(template, token => ResolveMovieToken(token, context, config, this.fileNameSanitizer), config);
        var cleaned = CleanBuiltString(replaced);

        if (config.ReplaceIllegalCharacters)
        {
            cleaned = this.fileNameSanitizer.SanitizeFolderName(
                cleaned,
                config.ColonReplacementFormat,
                config.CustomColonReplacementFormat);
        }
        else
        {
            cleaned = ApplyBaselineSafetyForFolder(cleaned);
        }

        return this.pathTruncator.TruncateFolderName(cleaned);
    }

    public static string BuildFileNameStatic(
        EpisodeNamingContext context,
        string pattern = null,
        NamingConfig namingConfig = null)
    {
        return new FileNameBuilder().BuildFileName(context, pattern, namingConfig);
    }

    public static string BuildFileNameStatic(
        MovieNamingContext context,
        string pattern = null,
        NamingConfig namingConfig = null)
    {
        return new FileNameBuilder().BuildFileName(context, pattern, namingConfig);
    }

    public static string BuildSeriesDirectoryStatic(
        EpisodeNamingContext context,
        string pattern = null,
        NamingConfig namingConfig = null)
    {
        return new FileNameBuilder().BuildSeriesDirectory(context, pattern, namingConfig);
    }

    public static string BuildSeasonDirectoryStatic(
        EpisodeNamingContext context,
        string pattern = null,
        NamingConfig namingConfig = null)
    {
        return new FileNameBuilder().BuildSeasonDirectory(context, pattern, namingConfig);
    }

    public static string BuildMovieDirectoryStatic(
        MovieNamingContext context,
        string pattern = null,
        NamingConfig namingConfig = null)
    {
        return new FileNameBuilder().BuildMovieDirectory(context, pattern, namingConfig);
    }

    private static string ReplaceTokens(
        string template,
        Func<Match, string> tokenResolver,
        NamingConfig config)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return TokenRegex.Replace(template, match =>
        {
            var value = tokenResolver(match);
            return NeutralizeTraversal(value, config);
        });
    }

    private static string ResolveEpisodeToken(Match match, EpisodeNamingContext context, NamingConfig config, IFileNameSanitizer sanitizer = null)
    {
        var tokenName = match.Groups["token"].Value.Trim();
        var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
        var normalizedToken = tokenName.Replace(" ", string.Empty).Replace(".", string.Empty).ToLowerInvariant();

        switch (normalizedToken)
        {
            case "seriestitle":
                return context.SeriesTitle ?? string.Empty;

            case "seriescleantitle":
                return !string.IsNullOrWhiteSpace(context.SeriesCleanTitle)
                    ? context.SeriesCleanTitle
                    : (sanitizer != null ? sanitizer.CleanTitle(context.SeriesTitle) : FileNameSanitizer.CleanTitleStatic(context.SeriesTitle));

            case "season":
                {
                    var fmt = ResolveIntegerTokenFormat(format, "0");
                    return context.SeasonNumber.ToString(fmt);
                }

            case "episode":
                {
                    var fmt = ResolveIntegerTokenFormat(format, "00");
                    var isLower = tokenName.Equals("e", StringComparison.Ordinal) || tokenName.StartsWith("e:", StringComparison.Ordinal);
                    if (context.EpisodeNumbers == null || context.EpisodeNumbers.Count == 0)
                    {
                        return 0.ToString(fmt);
                    }

                    return FormatMultiEpisode(context.EpisodeNumbers, fmt, config.MultiEpisodeStyle, isLower);
                }

            case "episodetitle":
                return FormatEpisodeTitles(context.EpisodeTitles);

            case "episodecleantitle":
                return FormatEpisodeCleanTitles(context.EpisodeCleanTitles, context.EpisodeTitles, sanitizer);

            case "absolute":
                {
                    var fmt = ResolveIntegerTokenFormat(format, "000");
                    return FormatAbsoluteEpisode(context.AbsoluteEpisodeNumbers, fmt, config.MultiEpisodeStyle);
                }

            case "airdate":
            case "air-date":
                return context.AirDate?.ToString(format ?? "yyyy-MM-dd") ?? string.Empty;

            case "qualitytitle":
                return context.Quality ?? string.Empty;

            case "qualityfull":
                return !string.IsNullOrWhiteSpace(context.QualityFull) ? context.QualityFull : (context.Quality ?? string.Empty);

            case "releasegroup":
                return context.ReleaseGroup ?? string.Empty;

            case "mediainfovideocodec":
            case "mediainfovideo":
                return context.VideoCodec ?? string.Empty;

            case "mediainfoaudiocodec":
            case "mediainfoaudio":
                return context.AudioCodec ?? string.Empty;

            case "mediainfoaudiochannels":
                return context.AudioChannels ?? string.Empty;

            case "mediainfohdrformat":
            case "mediainfohdr":
                return context.HdrFormat ?? string.Empty;

            case "mediainfovideodynamicrange":
                return context.VideoDynamicRange ?? string.Empty;

            case "originaltitle":
            case "originalfilename":
                return Path.GetFileNameWithoutExtension(context.OriginalFileName) ?? string.Empty;

            case "releaseyear":
            case "year":
                return context.ReleaseYear?.ToString(ResolveIntegerTokenFormat(format, "0000")) ?? string.Empty;

            case "imdbid":
                return context.ImdbId ?? string.Empty;

            case "tmdbid":
                return context.TmdbId ?? string.Empty;

            case "tvdbid":
                return context.TvdbId ?? string.Empty;

            default:
                return string.Empty;
        }
    }

    private static string ResolveMovieToken(Match match, MovieNamingContext context, NamingConfig config, IFileNameSanitizer sanitizer = null)
    {
        var tokenName = match.Groups["token"].Value.Trim();
        var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
        var normalizedToken = tokenName.Replace(" ", string.Empty).Replace(".", string.Empty).ToLowerInvariant();

        switch (normalizedToken)
        {
            case "movietitle":
                return context.MovieTitle ?? string.Empty;

            case "moviecleantitle":
                return !string.IsNullOrWhiteSpace(context.MovieCleanTitle)
                    ? context.MovieCleanTitle
                    : (sanitizer != null ? sanitizer.CleanTitle(context.MovieTitle) : FileNameSanitizer.CleanTitleStatic(context.MovieTitle));

            case "releaseyear":
            case "movieyear":
            case "year":
                return context.ReleaseYear > 0
                    ? context.ReleaseYear.ToString(ResolveIntegerTokenFormat(format, "0000"))
                    : string.Empty;

            case "editiontags":
            case "edition":
                return context.Edition ?? string.Empty;

            case "qualitytitle":
                return context.Quality ?? string.Empty;

            case "qualityfull":
                return !string.IsNullOrWhiteSpace(context.QualityFull) ? context.QualityFull : (context.Quality ?? string.Empty);

            case "releasegroup":
                return context.ReleaseGroup ?? string.Empty;

            case "mediainfovideocodec":
            case "mediainfovideo":
                return context.VideoCodec ?? string.Empty;

            case "mediainfoaudiocodec":
            case "mediainfoaudio":
                return context.AudioCodec ?? string.Empty;

            case "mediainfoaudiochannels":
                return context.AudioChannels ?? string.Empty;

            case "mediainfohdrformat":
            case "mediainfohdr":
                return context.HdrFormat ?? string.Empty;

            case "mediainfovideodynamicrange":
                return context.VideoDynamicRange ?? string.Empty;

            case "originaltitle":
            case "originalfilename":
                return Path.GetFileNameWithoutExtension(context.OriginalFileName) ?? string.Empty;

            case "imdbid":
                return context.ImdbId ?? string.Empty;

            case "tmdbid":
                return context.TmdbId ?? string.Empty;

            default:
                return string.Empty;
        }
    }

    private static string FormatMultiEpisode(
        List<int> episodeNumbers,
        string format,
        MultiEpisodeStyle style,
        bool isLower)
    {
        if (episodeNumbers == null || episodeNumbers.Count == 0)
        {
            return string.Empty;
        }

        if (episodeNumbers.Count == 1)
        {
            return episodeNumbers[0].ToString(format);
        }

        var epPrefix = isLower ? "e" : "E";
        var firstEpisode = episodeNumbers.Min();
        var lastEpisode = episodeNumbers.Max();

        return style switch
        {
            MultiEpisodeStyle.Extend =>
                $"{firstEpisode.ToString(format)}-{epPrefix}{lastEpisode.ToString(format)}",

            MultiEpisodeStyle.Range =>
                $"{firstEpisode.ToString(format)}-{lastEpisode.ToString(format)}",

            MultiEpisodeStyle.HyphenatedNumbers =>
                string.Join(string.Empty, episodeNumbers.Select((ep, idx) =>
                    idx == 0 ? ep.ToString(format) : $"-{epPrefix}{ep.ToString(format)}")),

            MultiEpisodeStyle.Scene =>
                string.Join(string.Empty, episodeNumbers.Select((ep, idx) =>
                    idx == 0 ? ep.ToString(format) : $".{epPrefix}{ep.ToString(format)}")),

            _ => $"{firstEpisode.ToString(format)}-{epPrefix}{lastEpisode.ToString(format)}",
        };
    }

    private static string FormatAbsoluteEpisode(
        List<int> absoluteNumbers,
        string format,
        MultiEpisodeStyle style)
    {
        if (absoluteNumbers == null || absoluteNumbers.Count == 0)
        {
            return string.Empty;
        }

        if (absoluteNumbers.Count == 1)
        {
            return absoluteNumbers[0].ToString(format);
        }

        var firstAbsolute = absoluteNumbers.Min();
        var lastAbsolute = absoluteNumbers.Max();

        return style switch
        {
            MultiEpisodeStyle.Extend or MultiEpisodeStyle.Range =>
                $"{firstAbsolute.ToString(format)}-{lastAbsolute.ToString(format)}",

            MultiEpisodeStyle.HyphenatedNumbers =>
                string.Join("-", absoluteNumbers.Select(num => num.ToString(format))),

            MultiEpisodeStyle.Scene =>
                string.Join(".", absoluteNumbers.Select(num => num.ToString(format))),

            _ => $"{firstAbsolute.ToString(format)}-{lastAbsolute.ToString(format)}",
        };
    }

    private static string FormatEpisodeTitles(List<string> titles)
    {
        if (titles == null || titles.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" + ", titles.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static string FormatEpisodeCleanTitles(List<string> cleanTitles, List<string> originalTitles, IFileNameSanitizer sanitizer = null)
    {
        if (cleanTitles != null && cleanTitles.Count > 0)
        {
            return string.Join(" + ", cleanTitles.Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        if (originalTitles != null && originalTitles.Count > 0)
        {
            return string.Join(" + ", originalTitles
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => sanitizer != null ? sanitizer.CleanTitle(t) : FileNameSanitizer.CleanTitleStatic(t)));
        }

        return string.Empty;
    }

    private static string NeutralizeTraversal(string value, NamingConfig config)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var neutralized = Regex.Replace(value, @"\.{2,}", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2))
            .Replace('/', '-')
            .Replace('\\', '-');

        return neutralized;
    }

    private static string CleanBuiltString(string built)
    {
        if (string.IsNullOrWhiteSpace(built))
        {
            return string.Empty;
        }

        // Remove drive prefixes (e.g. C:) and root slashes
        var cleaned = Regex.Replace(built, @"^[a-zA-Z]:[/\\]*", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
        cleaned = cleaned.TrimStart('/', '\\');

        // Neutralize traversal sequences and path separators in template
        cleaned = Regex.Replace(cleaned, @"\.{2,}", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2))
            .Replace('/', '-')
            .Replace('\\', '-');

        // Remove empty brackets and parentheses
        cleaned = EmptyBracketRegex.Replace(cleaned, string.Empty);
        cleaned = EmptyBracketRegex.Replace(cleaned, string.Empty);

        // Collapse repeated dashes
        cleaned = RepeatedDashesRegex.Replace(cleaned, " - ");

        // Collapse repeated spaces
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ");

        // Trim leading and trailing separators and whitespace
        cleaned = cleaned.Trim(' ', '-', '.', '_');

        return cleaned;
    }

    private static string ApplyBaselineSafetyForFile(string fullFileName)
    {
        if (string.IsNullOrWhiteSpace(fullFileName))
        {
            return "Unnamed";
        }

        var cleaned = RemoveControlChars(fullFileName);

        // Strip drive prefix and root slashes
        cleaned = Regex.Replace(cleaned, @"^[a-zA-Z]:[/\\]*", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
        cleaned = cleaned.TrimStart('/', '\\');

        // Strip traversal sequences and directory separators
        cleaned = Regex.Replace(cleaned, @"\.{2,}", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2))
            .Replace('/', '-')
            .Replace('\\', '-');

        // Replace colons with -
        cleaned = cleaned.Replace(":", "-");

        var lastDot = cleaned.LastIndexOf('.');
        string baseName;
        string extension;
        if (lastDot > 0)
        {
            baseName = cleaned.Substring(0, lastDot);
            extension = cleaned.Substring(lastDot);
        }
        else
        {
            baseName = cleaned;
            extension = string.Empty;
        }

        baseName = baseName.Trim(' ', '.');
        if (TorrentPathValidator.IsReservedDeviceName(baseName))
        {
            baseName = "_" + baseName;
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "_";
        }

        return baseName + extension;
    }

    private static string ApplyBaselineSafetyForFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return "Unnamed";
        }

        var cleaned = RemoveControlChars(folderName);

        // Strip drive prefix and root slashes
        cleaned = Regex.Replace(cleaned, @"^[a-zA-Z]:[/\\]*", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
        cleaned = cleaned.TrimStart('/', '\\');

        // Strip traversal sequences and directory separators
        cleaned = Regex.Replace(cleaned, @"\.{2,}", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2))
            .Replace('/', '-')
            .Replace('\\', '-');

        // Replace colons with -
        cleaned = cleaned.Replace(":", "-");

        cleaned = cleaned.Trim(' ', '.');
        if (TorrentPathValidator.IsReservedDeviceName(cleaned))
        {
            cleaned = "_" + cleaned;
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "_";
        }

        return cleaned;
    }

    private static string RemoveControlChars(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c > 0x1F && c != 0x7F)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string ResolveIntegerTokenFormat(string format, string defaultFormat)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return defaultFormat;
        }

        if (IsValidIntegerToStringFormat(format))
        {
            return format;
        }

        if (format.Equals("yyyy", StringComparison.OrdinalIgnoreCase) &&
            defaultFormat == "0000")
        {
            return "0000";
        }

        return defaultFormat;
    }

    private static bool IsValidIntegerToStringFormat(string format)
    {
        if (format.Any(c => c is '0' or '#'))
        {
            return true;
        }

        if (format.Length >= 1)
        {
            var specifier = format[0];
            if (specifier is 'D' or 'd' or 'G' or 'g' or 'N' or 'n' or 'X' or 'x' or 'F' or 'f' or 'E' or 'e' or 'C' or 'c')
            {
                return format.Length == 1 || format[1..].All(char.IsDigit);
            }
        }

        return false;
    }
}
