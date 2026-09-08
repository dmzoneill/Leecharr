// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Organizer;

public class FileNameBuilder : IFileNameBuilder
{
    private static readonly Regex TokenRegex = new(
        @"\{(?<token>[a-zA-Z0-9_\-\. ]+?)(?::(?<format>[a-zA-Z0-9_\-]+))?\}",
        RegexOptions.Compiled);

    private static readonly Regex EmptyBracketRegex = new(
        @"\[\s*[-_.]*\s*\]|\(\s*[-_.]*\s*\)|\{\s*[-_.]*\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex MultipleSpacesRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    private static readonly Regex RepeatedDashesRegex = new(
        @"\s*-\s*(?:-\s*)+",
        RegexOptions.Compiled);

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
            : (context.AirDate.HasValue && !context.EpisodeNumbers.Any()
                ? config.DailyEpisodeFormat
                : (context.AbsoluteEpisodeNumbers.Any() && !context.EpisodeNumbers.Any()
                    ? config.AnimeEpisodeFormat
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
                    var fmt = !string.IsNullOrWhiteSpace(format) ? format : "0";
                    return context.SeasonNumber.ToString(fmt);
                }

            case "episode":
                {
                    var fmt = !string.IsNullOrWhiteSpace(format) ? format : "00";
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
                    var fmt = !string.IsNullOrWhiteSpace(format) ? format : "000";
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
                return context.ReleaseYear?.ToString(format ?? "0000") ?? string.Empty;

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
                    ? context.ReleaseYear.ToString(format ?? "0000")
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

        return style switch
        {
            MultiEpisodeStyle.Extend =>
                $"{episodeNumbers.First().ToString(format)}-{epPrefix}{episodeNumbers.Last().ToString(format)}",

            MultiEpisodeStyle.Range =>
                $"{episodeNumbers.First().ToString(format)}-{episodeNumbers.Last().ToString(format)}",

            MultiEpisodeStyle.HyphenatedNumbers =>
                string.Join(string.Empty, episodeNumbers.Select((ep, idx) =>
                    idx == 0 ? ep.ToString(format) : $"-{epPrefix}{ep.ToString(format)}")),

            MultiEpisodeStyle.Scene =>
                string.Join(string.Empty, episodeNumbers.Select((ep, idx) =>
                    idx == 0 ? ep.ToString(format) : $".{epPrefix}{ep.ToString(format)}")),

            _ => $"{episodeNumbers.First().ToString(format)}-{epPrefix}{episodeNumbers.Last().ToString(format)}",
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

        return style switch
        {
            MultiEpisodeStyle.Extend or MultiEpisodeStyle.Range =>
                $"{absoluteNumbers.First().ToString(format)}-{absoluteNumbers.Last().ToString(format)}",

            MultiEpisodeStyle.HyphenatedNumbers =>
                string.Join("-", absoluteNumbers.Select(num => num.ToString(format))),

            MultiEpisodeStyle.Scene =>
                string.Join(".", absoluteNumbers.Select(num => num.ToString(format))),

            _ => $"{absoluteNumbers.First().ToString(format)}-{absoluteNumbers.Last().ToString(format)}",
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

        var neutralized = Regex.Replace(value, @"\.{2,}", string.Empty)
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

        // Remove empty brackets and parentheses
        var cleaned = EmptyBracketRegex.Replace(built, string.Empty);
        cleaned = EmptyBracketRegex.Replace(cleaned, string.Empty);

        // Collapse repeated dashes
        cleaned = RepeatedDashesRegex.Replace(cleaned, " - ");

        // Collapse repeated spaces
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ");

        // Trim leading and trailing separators and whitespace
        cleaned = cleaned.Trim(' ', '-', '.', '_');

        return cleaned;
    }
}
