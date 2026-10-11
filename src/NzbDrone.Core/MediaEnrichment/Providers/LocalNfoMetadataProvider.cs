// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class LocalNfoMetadataProvider : IMediaMetadataProvider
{
    private readonly IConfigService configService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public string ProviderId => "LocalNFO";

    public string DisplayName => "Local Filesystem NFO & Artwork Inspector";

    public string Version => "1.0.0";

    public string Description => "Parses zero-network local .nfo metadata, poster.jpg, and fanart.jpg directly from download folders.";

    public bool IsAvailable => true;

    public MediaMetadataCapabilities Capabilities => new()
    {
        SupportsMovies = true,
        SupportsTvSeries = true,
        SupportsMusic = true,
        SupportsPosters = true,
        SupportsFanart = true,
        SupportsCast = true,
        SupportsSeasonBanners = true,
        SupportsNfoParsing = true,
    };

    public LocalNfoMetadataProvider(IConfigService configService = null)
    {
        this.configService = configService;
    }

    public Task<MediaMetadataHealthCheckResult> ProbeHealthAsync()
    {
        return Task.FromResult(new MediaMetadataHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "Local NFO and artwork parser operational (offline mode).",
        });
    }

    public async Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null, string infoHash = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cleanTitle = CleanTitle(title);
        var parsedYear = year.HasValue && year.Value > 0 ? year.Value : ExtractYear(title);
        var isTv = ((category ?? string.Empty).Contains("tv", StringComparison.OrdinalIgnoreCase) ||
                    (category ?? string.Empty).Contains("show", StringComparison.OrdinalIgnoreCase) ||
                    (category ?? string.Empty).Contains("series", StringComparison.OrdinalIgnoreCase) ||
                    (category ?? string.Empty).Contains("sonarr", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(title) && Regex.IsMatch(title, @"(?i)\b(S\d{1,2}(?:E\d{1,3})?|\d{1,2}x\d{1,3}|Season[.\s_-]*(?!19\d\d|20\d\d)\d+|Episode[.\s_-]*\d+|E\d{2,3})\b", RegexOptions.None, TimeSpan.FromSeconds(2))))
                    && !(category ?? string.Empty).Contains("movie", StringComparison.OrdinalIgnoreCase)
                    && !(category ?? string.Empty).Contains("radarr", StringComparison.OrdinalIgnoreCase);
        var isMovie = !isTv;

        var meta = new MediaMetadata
        {
            Title = cleanTitle,
            Year = parsedYear,
            MediaType = isMovie ? "Movie" : "TV",
            Overview = string.Empty,
        };

        var mediaDirectory = this.LocateMediaDirectory(title);
        var nfoFilePath = this.LocateNfoFile(title);
        if (!string.IsNullOrWhiteSpace(nfoFilePath) && File.Exists(nfoFilePath))
        {
            try
            {
                var nfoContent = await File.ReadAllTextAsync(nfoFilePath);
                ParseNfoContent(nfoContent, meta);
                this.logger.Debug("Successfully parsed NFO file: {0}", nfoFilePath);

                var nfoDirectory = Path.GetDirectoryName(nfoFilePath);
                if (!string.IsNullOrWhiteSpace(nfoDirectory) && Directory.Exists(nfoDirectory))
                {
                    mediaDirectory = nfoDirectory;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read or parse NFO file at '{0}'", nfoFilePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(mediaDirectory) && Directory.Exists(mediaDirectory))
        {
            InspectLocalArtwork(mediaDirectory, meta);
        }

        return meta;
    }

    private string LocateNfoFile(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            if (inputPath.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase))
            {
                return inputPath;
            }

            var parentDir = Path.GetDirectoryName(inputPath);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                return FindNfoInDirectory(parentDir);
            }
        }

        if (Directory.Exists(inputPath))
        {
            return FindNfoInDirectory(inputPath);
        }

        if (this.configService != null)
        {
            var searchDirs = new[] { this.configService.DownloadDir, this.configService.IncompleteDownloadDir }
                .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d));

            foreach (var dir in searchDirs)
            {
                var candidate = Path.Combine(dir, inputPath);
                if (Directory.Exists(candidate))
                {
                    var found = FindNfoInDirectory(candidate);
                    if (found != null)
                    {
                        return found;
                    }
                }
                else if (File.Exists(candidate))
                {
                    if (candidate.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private string LocateMediaDirectory(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return null;
        }

        if (File.Exists(inputPath))
        {
            if (inputPath.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(inputPath);
            }

            var parentDir = Path.GetDirectoryName(inputPath);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                return parentDir;
            }
        }

        if (Directory.Exists(inputPath))
        {
            return inputPath;
        }

        if (this.configService != null)
        {
            var searchDirs = new[] { this.configService.DownloadDir, this.configService.IncompleteDownloadDir }
                .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d));

            foreach (var dir in searchDirs)
            {
                var candidate = Path.Combine(dir, inputPath);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                if (File.Exists(candidate))
                {
                    var parent = Path.GetDirectoryName(candidate);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        return parent;
                    }
                }
            }
        }

        return null;
    }

    private static string FindNfoInDirectory(string directory)
    {
        try
        {
            var movieNfo = Path.Combine(directory, "movie.nfo");
            if (File.Exists(movieNfo))
            {
                return movieNfo;
            }

            var tvNfo = Path.Combine(directory, "tvshow.nfo");
            if (File.Exists(tvNfo))
            {
                return tvNfo;
            }

            return Directory.GetFiles(directory, "*.nfo", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void ParseNfoContent(string xmlContent, MediaMetadata meta)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return;
        }

        try
        {
            var doc = SafeXmlParser.Parse(xmlContent);
            var root = doc.Root;
            if (root != null)
            {
                var titleElem = root.Element("title");
                if (titleElem != null && !string.IsNullOrWhiteSpace(titleElem.Value))
                {
                    meta.Title = titleElem.Value.Trim();
                }

                var yearElem = root.Element("year");
                if (yearElem != null && int.TryParse(yearElem.Value.Trim(), out var y) && y > 0)
                {
                    meta.Year = y;
                }

                var plotElem = root.Element("plot") ?? root.Element("outline");
                if (plotElem != null && !string.IsNullOrWhiteSpace(plotElem.Value))
                {
                    meta.Overview = plotElem.Value.Trim();
                }

                var ratingElem = root.Element("rating");
                if (ratingElem != null)
                {
                    var ratingText = ratingElem.Element("value")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(ratingText))
                    {
                        ratingText = ratingElem.Value?.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(ratingText) &&
                        double.TryParse(ratingText, NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                    {
                        meta.Rating = r;
                    }
                }

                if (meta.Rating == 0)
                {
                    var ratingsContainer = root.Element("ratings");
                    if (ratingsContainer != null)
                    {
                        var ratingEntries = ratingsContainer.Elements("rating").ToList();
                        var selectedRating = ratingEntries.FirstOrDefault(entry =>
                                string.Equals(entry.Attribute("default")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                            ?? ratingEntries.FirstOrDefault();

                        if (selectedRating != null)
                        {
                            var nestedRatingText = selectedRating.Element("value")?.Value?.Trim()
                                ?? selectedRating.Value?.Trim();
                            if (!string.IsNullOrWhiteSpace(nestedRatingText) &&
                                double.TryParse(nestedRatingText, NumberStyles.Any, CultureInfo.InvariantCulture, out var nestedRating))
                            {
                                meta.Rating = nestedRating;
                            }
                        }
                    }
                }

                var genreElems = root.Elements("genre")
                    .Select(genre => genre.Value?.Trim())
                    .Where(genre => !string.IsNullOrWhiteSpace(genre))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (genreElems.Count > 0)
                {
                    meta.Genres = string.Join(", ", genreElems);
                }

                var imdbElem = root.Element("id") ?? root.Element("imdbid");
                if (imdbElem != null && !string.IsNullOrWhiteSpace(imdbElem.Value))
                {
                    meta.ImdbId = imdbElem.Value.Trim();
                }

                var tmdbElem = root.Element("tmdbid");
                if (tmdbElem != null && !string.IsNullOrWhiteSpace(tmdbElem.Value))
                {
                    meta.TmdbId = tmdbElem.Value.Trim();
                }

                if (string.Equals(root.Name.LocalName, "tvshow", StringComparison.OrdinalIgnoreCase))
                {
                    meta.MediaType = "TV";
                }
                else if (string.Equals(root.Name.LocalName, "movie", StringComparison.OrdinalIgnoreCase))
                {
                    meta.MediaType = "Movie";
                }

                var actorElems = root.Descendants("actor");
                var castList = new System.Collections.Generic.List<string>();
                foreach (var actor in actorElems)
                {
                    var name = actor.Element("name")?.Value?.Trim() ?? (!actor.HasElements ? actor.Value?.Trim() : null);
                    var role = actor.Element("role")?.Value?.Trim();
                    var entry = !string.IsNullOrWhiteSpace(role) ? $"{name} as {role}" : name;
                    if (!string.IsNullOrWhiteSpace(entry) && !castList.Contains(entry, StringComparer.OrdinalIgnoreCase))
                    {
                        castList.Add(entry);
                        if (castList.Count >= 10)
                        {
                            break;
                        }
                    }
                }

                if (castList.Count > 0)
                {
                    meta.Cast = castList;
                }

                return;
            }
        }
        catch
        {
            // Fall back to regex parsing if XML parsing fails due to non-standard or malformed NFO text
        }

        var titleMatch = Regex.Match(xmlContent, @"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        if (titleMatch.Success && !string.IsNullOrWhiteSpace(titleMatch.Groups[1].Value))
        {
            meta.Title = titleMatch.Groups[1].Value.Trim();
        }

        var yearMatch = Regex.Match(xmlContent, @"<year>(\d{4})</year>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var yearVal) && yearVal > 0)
        {
            meta.Year = yearVal;
        }

        var plotMatch = Regex.Match(xmlContent, @"<(plot|outline)>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        if (plotMatch.Success && !string.IsNullOrWhiteSpace(plotMatch.Groups[2].Value))
        {
            meta.Overview = plotMatch.Groups[2].Value.Trim();
        }

        if (meta.Rating == 0)
        {
            var nestedRatingMatch = Regex.Match(
                xmlContent,
                @"<ratings>.*?<rating[^>]*>.*?<value>([\d.]+)</value>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(2));
            if (nestedRatingMatch.Success &&
                double.TryParse(nestedRatingMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var nestedRatingVal))
            {
                meta.Rating = nestedRatingVal;
            }
            else
            {
                var ratingMatch = Regex.Match(xmlContent, @"<rating>([\d.]+)</rating>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                if (ratingMatch.Success &&
                    double.TryParse(ratingMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ratingVal))
                {
                    meta.Rating = ratingVal;
                }
            }
        }

        if (string.IsNullOrEmpty(meta.Genres))
        {
            var genreMatches = Regex.Matches(xmlContent, @"<genre>(.*?)</genre>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
            if (genreMatches.Count > 0)
            {
                var genreList = new System.Collections.Generic.List<string>();
                foreach (Match genreMatch in genreMatches)
                {
                    var genre = genreMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(genre) && !genreList.Contains(genre, StringComparer.OrdinalIgnoreCase))
                    {
                        genreList.Add(genre);
                    }
                }

                if (genreList.Count > 0)
                {
                    meta.Genres = string.Join(", ", genreList);
                }
            }
        }

        var actorMatches = Regex.Matches(xmlContent, @"<actor>(?:(?!</actor>).)*?<name>(.*?)</name>(?:(?:(?!</actor>).)*?<role>(.*?)</role>)?", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        if (actorMatches.Count > 0)
        {
            var castList = new System.Collections.Generic.List<string>();
            foreach (Match m in actorMatches)
            {
                var name = m.Groups[1].Value.Trim();
                var role = m.Groups.Count > 2 && m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
                var entry = !string.IsNullOrWhiteSpace(role) ? $"{name} as {role}" : name;
                if (!string.IsNullOrWhiteSpace(entry) && !castList.Contains(entry, StringComparer.OrdinalIgnoreCase))
                {
                    castList.Add(entry);
                    if (castList.Count >= 10)
                    {
                        break;
                    }
                }
            }

            if (castList.Count > 0)
            {
                meta.Cast = castList;
            }
        }
    }

    private void InspectLocalArtwork(string directory, MediaMetadata meta)
    {
        try
        {
            var posterCandidates = new[] { "poster.jpg", "poster.png", "cover.jpg", "cover.png", "folder.jpg" };
            foreach (var name in posterCandidates)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    meta.PosterUrl = path;
                    break;
                }
            }

            var fanartCandidates = new[] { "fanart.jpg", "fanart.png", "backdrop.jpg", "backdrop.png", "background.jpg" };
            foreach (var name in fanartCandidates)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    meta.BackdropUrl = path;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Error scanning local poster/fanart images in {Directory}", directory);
        }
    }

    internal static string CleanTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(rawTitle, @"[._]", " ", RegexOptions.None, TimeSpan.FromSeconds(2));

        // Strip TV season/episode markers (e.g. S01E01, S01E01-E04, S01E01E02, 1x05, Season 1, Episode 01, E05)
        cleaned = Regex.Replace(cleaned, @"(?i)(?<!^)\s*\b(S\d{1,2}(?:[-._]?(?:E|EP)\d{1,3}(?:(?:[-_~]|e|E|\.E)\d{1,3})*)?|\d{1,2}x\d{1,3}|Season\s*\d+|Episode\s*\d+|E\d{2,3})\b.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        // Strip edition tags if not at start of title and followed by quality tags, years, or end of string
        cleaned = Regex.Replace(cleaned, @"(?i)(?<!^)\s*\b(repack|proper|internal|extended|unrated|multi|complete|limited|theatrical|remastered|director'?s\s*cut)\b(?=\s+(?:1080p|720p|2160p|4k|8k|uhd|hdr|remux|bluray|blu-ray|web|webrip|web-dl|hdtv|dvdrip|bdrip|x264|x265|hevc|h264|h265|dts|aac|edition|cut|version|series|season|\d{4}|$)|$).*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        // Strip unambiguous quality/source/codec tags
        cleaned = Regex.Replace(cleaned, @"(?i)(?<!^)\s*\b(2160p|1080p|1080i|720p|576p|480p|4k|8k|uhd|hdr|remux|bluray|blu-ray|web-dl|webrip|web-?dl|web-?rip|hdtv|dvdrip|bdrip|x264|x265|hevc|h264|h265|avc|xvid|divx|10bit)\b.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        var year = ExtractYear(rawTitle);
        if (year > 0)
        {
            cleaned = Regex.Replace(cleaned, $@"(?<!^)\s*\b{year}\b.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        else
        {
            cleaned = Regex.Replace(cleaned, @"(?<!^)\s*\b(19\d\d|20\d\d)\b.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
        }

        cleaned = cleaned.Trim('-', ' ', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? rawTitle.Trim() : cleaned.Trim();
    }

    private static int ExtractYear(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return 0;
        }

        var taggedMatch = Regex.Match(
            rawTitle,
            @"\b(19\d\d|20\d\d)\b(?=[.\s_]*(?:1080p|720p|2160p|4k|uhd|hdr|remux|bluray|web|dvd|x264|x265|hevc|h264|h265|\(|$))",
            RegexOptions.IgnoreCase | RegexOptions.RightToLeft,
            TimeSpan.FromSeconds(2));
        if (taggedMatch.Success && int.TryParse(taggedMatch.Value, out var ty) && ty >= 1900 && ty <= DateTime.UtcNow.Year + 2)
        {
            return ty;
        }

        var rightmostMatch = Regex.Match(rawTitle, @"\b(19\d\d|20\d\d)\b", RegexOptions.RightToLeft, TimeSpan.FromSeconds(2));
        return rightmostMatch.Success && int.TryParse(rightmostMatch.Value, out var y) ? y : 0;
    }
}
