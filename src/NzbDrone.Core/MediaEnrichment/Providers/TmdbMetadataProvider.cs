// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
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
            Overview = string.Empty,
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

        if (!string.IsNullOrEmpty(id))
        {
            try
            {
                var detailsEndpoint = isMovie
                    ? $"https://api.themoviedb.org/3/movie/{Uri.EscapeDataString(id)}?api_key={Uri.EscapeDataString(apiKey)}&append_to_response=credits,external_ids"
                    : $"https://api.themoviedb.org/3/tv/{Uri.EscapeDataString(id)}?api_key={Uri.EscapeDataString(apiKey)}&append_to_response=credits,external_ids";

                using var detailsReq = new HttpRequestMessage(HttpMethod.Get, detailsEndpoint);
                using var detailsResp = await this.httpClient.SendAsync(detailsReq);
                if (detailsResp.IsSuccessStatusCode)
                {
                    var detailsJson = await detailsResp.Content.ReadAsStringAsync();
                    using var detailsDoc = JsonDocument.Parse(detailsJson);
                    var root = detailsDoc.RootElement;

                    if (root.TryGetProperty(titleProperty, out var dt) && !string.IsNullOrWhiteSpace(dt.GetString()))
                    {
                        meta.Title = dt.GetString();
                    }

                    if (root.TryGetProperty("overview", out var dov) && !string.IsNullOrWhiteSpace(dov.GetString()))
                    {
                        meta.Overview = dov.GetString();
                    }

                    if (root.TryGetProperty("vote_average", out var dr) && dr.TryGetDouble(out var drate))
                    {
                        meta.Rating = drate;
                    }

                    if (root.TryGetProperty(releaseDateProperty, out var drd) && !string.IsNullOrWhiteSpace(drd.GetString()))
                    {
                        var match = Regex.Match(drd.GetString(), @"^(19\d\d|20\d\d)");
                        if (match.Success && int.TryParse(match.Value, out var y))
                        {
                            meta.Year = y;
                        }
                    }

                    if (root.TryGetProperty("poster_path", out var dp) && !string.IsNullOrWhiteSpace(dp.GetString()))
                    {
                        meta.PosterUrl = $"https://image.tmdb.org/t/p/original{dp.GetString()}";
                    }

                    if (root.TryGetProperty("backdrop_path", out var db) && !string.IsNullOrWhiteSpace(db.GetString()))
                    {
                        meta.BackdropUrl = $"https://image.tmdb.org/t/p/original{db.GetString()}";
                    }

                    if (root.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array)
                    {
                        meta.Genres = string.Join(", ", g.EnumerateArray()
                            .Where(x => x.TryGetProperty("name", out var gn) && gn.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetProperty("name").GetString()));
                    }

                    if (root.TryGetProperty("external_ids", out var extIds) && extIds.ValueKind == JsonValueKind.Object)
                    {
                        if (extIds.TryGetProperty("imdb_id", out var imdbProp) && imdbProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(imdbProp.GetString()))
                        {
                            meta.ImdbId = imdbProp.GetString();
                        }

                        if (extIds.TryGetProperty("tvdb_id", out var tvdbProp))
                        {
                            if (tvdbProp.ValueKind == JsonValueKind.Number && tvdbProp.TryGetInt32(out var tvdbInt) && tvdbInt > 0)
                            {
                                meta.TvdbId = tvdbInt.ToString();
                            }
                            else if (tvdbProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tvdbProp.GetString()))
                            {
                                meta.TvdbId = tvdbProp.GetString();
                            }
                        }
                    }

                    if (root.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
                    {
                        if (credits.TryGetProperty("cast", out var castArray) && castArray.ValueKind == JsonValueKind.Array)
                        {
                            meta.Cast = castArray.EnumerateArray()
                                .Where(c => c.TryGetProperty("name", out var cn) && cn.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cn.GetString()))
                                .Select(c => c.GetProperty("name").GetString())
                                .Take(10)
                                .ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to fetch extended details from TMDB API for id {0}", id);
            }
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

        // Strip TV season/episode markers (e.g. S01E01, S01E01-E04, S01E01E02, 1x05, Season 1, Episode 01, E05)
        cleaned = Regex.Replace(cleaned, @"(?i)(?<!^)\s*\b(S\d{1,2}(?:[-._]?(?:E|EP)\d{1,3}(?:(?:[-_~]|e|E|\.E)\d{1,3})*)?|\d{1,2}x\d{1,3}|Season\s*\d+|Episode\s*\d+|E\d{2,3})\b.*$", string.Empty);

        // Strip edition tags if not at start of title and followed by quality tags, years, or end of string
        cleaned = Regex.Replace(cleaned, @"(?i)(?<!^)\s*\b(repack|proper|internal|extended|unrated|multi|complete|limited|theatrical|remastered|director'?s\s*cut)\b(?=\s+(?:1080p|720p|2160p|4k|8k|uhd|hdr|remux|bluray|blu-ray|web|webrip|web-dl|hdtv|dvdrip|bdrip|x264|x265|hevc|h264|h265|dts|aac|edition|cut|version|series|season|\d{4}|$)|$).*$", string.Empty);

        // Strip unambiguous quality/source/codec tags
        cleaned = Regex.Replace(cleaned, @"(?i)(?<!^)\s*\b(2160p|1080p|1080i|720p|576p|480p|4k|8k|uhd|hdr|remux|bluray|blu-ray|web-dl|webrip|web-?dl|web-?rip|hdtv|dvdrip|bdrip|x264|x265|hevc|h264|h265|avc|xvid|divx|10bit)\b.*$", string.Empty);

        var year = ExtractYear(rawTitle);
        if (year > 0)
        {
            cleaned = Regex.Replace(cleaned, $@"(?<!^)\s*\b{year}\b.*$", string.Empty);
        }
        else
        {
            cleaned = Regex.Replace(cleaned, @"(?<!^)\s*\b(19\d\d|20\d\d)\b.*$", string.Empty);
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
            RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        if (taggedMatch.Success && int.TryParse(taggedMatch.Value, out var ty) && ty >= 1900 && ty <= DateTime.UtcNow.Year + 2)
        {
            return ty;
        }

        var rightmostMatch = Regex.Match(rawTitle, @"\b(19\d\d|20\d\d)\b", RegexOptions.RightToLeft);
        return rightmostMatch.Success && int.TryParse(rightmostMatch.Value, out var y) ? y : 0;
    }
}
