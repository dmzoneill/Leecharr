// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
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

    public async Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null)
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
                    (!string.IsNullOrEmpty(title) && Regex.IsMatch(title, @"(?i)\b(S\d{1,2}(?:E\d{1,3})?|\d{1,2}x\d{1,3}|Season[.\s_-]*(?!19\d\d|20\d\d)\d+|Episode[.\s_-]*\d+|E\d{2,3})\b")))
                   && !(category ?? string.Empty).Contains("movie", StringComparison.OrdinalIgnoreCase)
                   && !(category ?? string.Empty).Contains("radarr", StringComparison.OrdinalIgnoreCase);
        var isMovie = !isTv;

        var meta = new MediaMetadata
        {
            Title = cleanTitle,
            Year = parsedYear,
            MediaType = isMovie ? "Movie" : "TV",
            Overview = $"Parsed from local media directory files for {cleanTitle}.",
        };

        var nfoFilePath = this.LocateNfoFile(title);
        if (!string.IsNullOrWhiteSpace(nfoFilePath) && File.Exists(nfoFilePath))
        {
            try
            {
                var nfoContent = await File.ReadAllTextAsync(nfoFilePath);
                ParseNfoContent(nfoContent, meta);
                this.logger.Debug("Successfully parsed NFO file: {0}", nfoFilePath);

                var dir = Path.GetDirectoryName(nfoFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    InspectLocalArtwork(dir, meta);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read or parse NFO file at '{0}'", nfoFilePath);
            }
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
            var doc = XDocument.Parse(xmlContent);
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
                if (ratingElem != null && double.TryParse(ratingElem.Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                {
                    meta.Rating = r;
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

                return;
            }
        }
        catch
        {
            // Fall back to regex parsing if XML parsing fails due to non-standard or malformed NFO text
        }

        var titleMatch = Regex.Match(xmlContent, @"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (titleMatch.Success && !string.IsNullOrWhiteSpace(titleMatch.Groups[1].Value))
        {
            meta.Title = titleMatch.Groups[1].Value.Trim();
        }

        var yearMatch = Regex.Match(xmlContent, @"<year>(\d{4})</year>", RegexOptions.IgnoreCase);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var yearVal) && yearVal > 0)
        {
            meta.Year = yearVal;
        }

        var plotMatch = Regex.Match(xmlContent, @"<(plot|outline)>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (plotMatch.Success && !string.IsNullOrWhiteSpace(plotMatch.Groups[2].Value))
        {
            meta.Overview = plotMatch.Groups[2].Value.Trim();
        }

        var ratingMatch = Regex.Match(xmlContent, @"<rating>([\d.]+)</rating>", RegexOptions.IgnoreCase);
        if (ratingMatch.Success && double.TryParse(ratingMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ratingVal))
        {
            meta.Rating = ratingVal;
        }
    }

    private static void InspectLocalArtwork(string directory, MediaMetadata meta)
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
        catch
        {
        }
    }

    internal static string CleanTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(rawTitle, @"[._]", " ");
        cleaned = Regex.Replace(cleaned, @"(?i)\b(S\d+(?:E\d+)?|\d+x\d+|Season\s*\d+|Episode\s*\d+|E\d{2,3})\b.*$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?i)\b(1080p|720p|2160p|4k|uhd|hdr|remux|bluray|web-dl|webrip|x264|x265|hevc|h264|h265|dts|aac|repack|proper|internal|extended|unrated|multi|complete)\b.*$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\b(19\d\d|20\d\d)\b.*", string.Empty);
        cleaned = cleaned.Trim('-', ' ', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? rawTitle.Trim() : cleaned.Trim();
    }

    private static int ExtractYear(string rawTitle)
    {
        var match = Regex.Match(rawTitle, @"\b(19\d\d|20\d\d)\b");
        return match.Success && int.TryParse(match.Value, out var y) ? y : 0;
    }
}
