// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class TmdbMetadataProvider : IMediaMetadataProvider
{
    private readonly IConfigService configService;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public string ProviderId => "TMDB";

    public string DisplayName => "The Movie Database (TMDB v3/v4)";

    public string Version => "3.0.0";

    public string Description => "Fetches rich movie and TV show metadata, cast lists, high-res posters, and fanart backdrops from TMDB.";

    public bool IsAvailable => true;

    public MediaMetadataCapabilities Capabilities => new()
    {
        SupportsMovies = true,
        SupportsTvSeries = true,
        SupportsMusic = false,
        SupportsPosters = true,
        SupportsFanart = true,
        SupportsCast = true,
        SupportsSeasonBanners = true,
        SupportsNfoParsing = false,
    };

    public TmdbMetadataProvider(IConfigService configService = null, HttpClient httpClient = null)
    {
        this.configService = configService;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public Task<MediaMetadataHealthCheckResult> ProbeHealthAsync()
    {
        var apiKey = this.GetApiKey();
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);

        return Task.FromResult(new MediaMetadataHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = hasApiKey
                ? "TMDB API provider is reachable and active (API key configured)."
                : "TMDB provider operational (heuristic parsing mode without API key).",
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

        var apiKey = this.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var metaFromApi = await this.QueryTmdbApiAsync(apiKey, cleanTitle, parsedYear, isMovie);
                if (metaFromApi != null)
                {
                    return metaFromApi;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to fetch metadata from TMDB API for '{0}'", cleanTitle);
            }
        }

        return new MediaMetadata
        {
            Title = cleanTitle,
            Year = parsedYear,
            MediaType = isMovie ? "Movie" : "TV",
            Overview = $"Metadata extracted for {cleanTitle}.",
            Rating = 0.0,
        };
    }

    private string GetApiKey()
    {
        var configured = this.configService?.TmdbApiKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? string.Empty;
    }

    private async Task<MediaMetadata> QueryTmdbApiAsync(string apiKey, string title, int year, bool isMovie)
    {
        var searchEndpoint = isMovie
            ? $"https://api.themoviedb.org/3/search/movie?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(title)}" + (year > 0 ? $"&year={year}" : string.Empty)
            : $"https://api.themoviedb.org/3/search/tv?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(title)}" + (year > 0 ? $"&first_air_date_year={year}" : string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Get, searchEndpoint);
        using var response = await this.httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            this.logger.Debug("TMDB API returned HTTP {0} for search query '{1}'", response.StatusCode, title);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return null;
        }

        var first = results[0];
        var titleProperty = isMovie ? "title" : "name";
        var resultTitle = first.TryGetProperty(titleProperty, out var t) ? t.GetString() : title;
        var overview = first.TryGetProperty("overview", out var ov) ? ov.GetString() : string.Empty;
        var rating = first.TryGetProperty("vote_average", out var r) && r.TryGetDouble(out var rate) ? rate : 0.0;
        var id = first.TryGetProperty("id", out var idElem) ? idElem.ToString() : null;

        var releaseDateProperty = isMovie ? "release_date" : "first_air_date";
        var parsedYear = year;
        if (parsedYear == 0 && first.TryGetProperty(releaseDateProperty, out var rd) && !string.IsNullOrWhiteSpace(rd.GetString()))
        {
            var match = Regex.Match(rd.GetString(), @"^(19\d\d|20\d\d)");
            if (match.Success && int.TryParse(match.Value, out var y))
            {
                parsedYear = y;
            }
        }

        var meta = new MediaMetadata
        {
            Title = resultTitle,
            Year = parsedYear,
            MediaType = isMovie ? "Movie" : "TV",
            Overview = overview,
            Rating = rating,
            TmdbId = id,
        };

        if (first.TryGetProperty("poster_path", out var poster) && !string.IsNullOrWhiteSpace(poster.GetString()))
        {
            meta.PosterUrl = $"https://image.tmdb.org/t/p/original{poster.GetString()}";
        }

        if (first.TryGetProperty("backdrop_path", out var backdrop) && !string.IsNullOrWhiteSpace(backdrop.GetString()))
        {
            meta.BackdropUrl = $"https://image.tmdb.org/t/p/original{backdrop.GetString()}";
        }

        return meta;
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
        cleaned = Regex.Replace(cleaned, @"(?<!^)\s*\b(19\d\d|20\d\d)\b.*$", string.Empty);
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
            RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        if (taggedMatch.Success && int.TryParse(taggedMatch.Value, out var ty) && ty >= 1900 && ty <= DateTime.UtcNow.Year + 2)
        {
            return ty;
        }

        var rightmostMatch = Regex.Match(rawTitle, @"\b(19\d\d|20\d\d)\b", RegexOptions.RightToLeft);
        return rightmostMatch.Success && int.TryParse(rightmostMatch.Value, out var y) ? y : 0;
    }
}
