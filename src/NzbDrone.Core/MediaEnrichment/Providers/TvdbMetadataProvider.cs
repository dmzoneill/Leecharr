// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class TvdbMetadataProvider : IMediaMetadataProvider
{
    private readonly IConfigService configService;
    private readonly IArrConnectionRepository arrRepository;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string cachedToken;
    private DateTime tokenExpirationUtc = DateTime.MinValue;

    public string ProviderId => "TheTVDB";

    public string DisplayName => "TheTVDB API v4";

    public string Version => "4.0.0";

    public string Description => "Fetches TV series episodic metadata, season posters, series overviews, and actor credits from TheTVDB.";

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

    public TvdbMetadataProvider(
        IConfigService configService = null,
        IArrConnectionRepository arrRepository = null,
        HttpClient httpClient = null)
    {
        this.configService = configService;
        this.arrRepository = arrRepository;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<MediaMetadataHealthCheckResult> ProbeHealthAsync()
    {
        var apiKey = this.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var token = await this.GetBearerTokenAsync(apiKey, this.GetPin());
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return new MediaMetadataHealthCheckResult
                    {
                        IsHealthy = true,
                        StatusMessage = "TheTVDB v4 API provider is active and configured.",
                    };
                }

                return new MediaMetadataHealthCheckResult
                {
                    IsHealthy = false,
                    StatusMessage = "TheTVDB v4 login failed; check API key and subscriber PIN.",
                };
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "TheTVDB metadata health probe failed");
                return new MediaMetadataHealthCheckResult
                {
                    IsHealthy = false,
                    StatusMessage = $"TheTVDB API health probe failed: {ex.Message}",
                };
            }
        }

        var enabled = this.arrRepository?.GetEnabled()?.ToList() ?? new List<ArrConnectionDefinition>();
        if (enabled.Count == 0)
        {
            return new MediaMetadataHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = "TheTVDB API key is not configured and no enabled Arr instances are available for Servarr fallback.",
            };
        }

        var reachable = 0;
        var warnings = new List<string>();
        foreach (var conn in enabled)
        {
            if (await this.ProbeArrConnectionForHealthAsync(conn))
            {
                reachable++;
            }
            else
            {
                warnings.Add($"{conn.Name ?? conn.ArrType} ({conn.Url}) is not reachable for Servarr fallback.");
            }
        }

        if (reachable == 0)
        {
            return new MediaMetadataHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = "TheTVDB API key is not configured and no enabled Arr instances responded to a system status probe.",
                Warnings = warnings,
            };
        }

        var result = new MediaMetadataHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = $"TheTVDB provider operational with Servarr fallback ({reachable}/{enabled.Count} enabled Arr instances reachable).",
        };

        if (warnings.Count > 0)
        {
            result.Warnings = warnings;
        }

        return result;
    }

    private async Task<bool> ProbeArrConnectionForHealthAsync(ArrConnectionDefinition conn)
    {
        if (string.IsNullOrWhiteSpace(conn?.Url))
        {
            return false;
        }

        var baseUrl = conn.Url.TrimEnd('/');
        var endpoints = string.Equals(conn.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase)
            ? new[] { "api/v1/system/status", "api/v3/system/status" }
            : new[] { "api/v3/system/status", "api/v1/system/status" };

        foreach (var endpoint in endpoints)
        {
            try
            {
                var uri = new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), endpoint);
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                if (!string.IsNullOrWhiteSpace(conn.ApiKey))
                {
                    req.Headers.Add("X-Api-Key", conn.ApiKey);
                }

                using var response = await this.httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "TheTVDB Servarr fallback health probe failed for {0}", conn.Name);
            }
        }

        return false;
    }

    public async Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null, string infoHash = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cleanTitle = CleanTvTitle(title);
        var parsedYear = year.HasValue && year.Value > 0 ? year.Value : ExtractYear(title);

        var isMovie = ((category ?? string.Empty).Contains("movie", StringComparison.OrdinalIgnoreCase) ||
                        (category ?? string.Empty).Contains("radarr", StringComparison.OrdinalIgnoreCase)) &&
                        !(category ?? string.Empty).Contains("tv", StringComparison.OrdinalIgnoreCase) &&
                        !(category ?? string.Empty).Contains("series", StringComparison.OrdinalIgnoreCase);

        var apiKey = this.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var pin = this.GetPin();
                var token = await this.GetBearerTokenAsync(apiKey, pin);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    var metaFromApi = await this.QueryTvdbApiAsync(token, cleanTitle, parsedYear, isMovie);
                    if (metaFromApi != null)
                    {
                        return metaFromApi;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to fetch metadata from TheTVDB v4 API for '{0}'", cleanTitle);
            }
        }

        // Fallback to configured Servarr instances (Sonarr / Radarr)
        if (this.arrRepository != null)
        {
            try
            {
                var servarrMeta = await this.FetchFromServarrAsync(cleanTitle, isMovie, parsedYear, infoHash);
                if (servarrMeta != null)
                {
                    return servarrMeta;
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Servarr fallback lookup failed for '{0}'", cleanTitle);
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
        var configured = this.configService?.TvdbApiKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Environment.GetEnvironmentVariable("TVDB_API_KEY")
            ?? Environment.GetEnvironmentVariable("THE_TVDB_API_KEY")
            ?? Environment.GetEnvironmentVariable("TVDB_V4_API_KEY")
            ?? string.Empty;
    }

    private string GetPin()
    {
        var configured = this.configService?.TvdbPin;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Environment.GetEnvironmentVariable("TVDB_PIN") ?? string.Empty;
    }

    private async Task<string> GetBearerTokenAsync(string apiKey, string pin)
    {
        if (!string.IsNullOrEmpty(this.cachedToken) && DateTime.UtcNow < this.tokenExpirationUtc)
        {
            return this.cachedToken;
        }

        await this.tokenLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(this.cachedToken) && DateTime.UtcNow < this.tokenExpirationUtc)
            {
                return this.cachedToken;
            }

            var loginPayload = new Dictionary<string, string>
            {
                { "apikey", apiKey },
            };

            if (!string.IsNullOrWhiteSpace(pin))
            {
                loginPayload["pin"] = pin;
            }

            var jsonBody = JsonSerializer.Serialize(loginPayload);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api4.thetvdb.com/v4/login")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            };

            using var resp = await this.httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                this.logger.Warn("TheTVDB login endpoint returned HTTP {0}", resp.StatusCode);
                return null;
            }

            var respJson = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respJson);
            if (doc.RootElement.TryGetProperty("data", out var dataElem) &&
                dataElem.TryGetProperty("token", out var tokenElem) &&
                tokenElem.ValueKind == JsonValueKind.String)
            {
                this.cachedToken = tokenElem.GetString();
                this.tokenExpirationUtc = DateTime.UtcNow.AddHours(23);
                return this.cachedToken;
            }

            return null;
        }
        finally
        {
            this.tokenLock.Release();
        }
    }

    private async Task<MediaMetadata> QueryTvdbApiAsync(string token, string title, int year, bool isMovie)
    {
        var searchEndpoint = $"https://api4.thetvdb.com/v4/search?query={Uri.EscapeDataString(title)}" +
                            (isMovie ? "&type=movie" : "&type=series") +
                            (year > 0 ? $"&year={year}" : string.Empty);

        using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchEndpoint);
        searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        searchReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var searchResp = await this.httpClient.SendAsync(searchReq);
        if (!searchResp.IsSuccessStatusCode)
        {
            this.logger.Debug("TheTVDB search returned HTTP {0} for '{1}'", searchResp.StatusCode, title);
            return null;
        }

        var searchJson = await searchResp.Content.ReadAsStringAsync();
        using var searchDoc = JsonDocument.Parse(searchJson);

        if (!searchDoc.RootElement.TryGetProperty("data", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var first = results[0];
        var resultTitle = first.TryGetProperty("name", out var n) ? n.GetString() : (first.TryGetProperty("title", out var t) ? t.GetString() : title);
        var overview = first.TryGetProperty("overview", out var ov) ? ov.GetString() : string.Empty;
        var rating = first.TryGetProperty("score", out var s) && s.TryGetDouble(out var rate) ? rate : 0.0;

        string rawTvdbId = null;
        if (first.TryGetProperty("tvdb_id", out var tidElem))
        {
            rawTvdbId = tidElem.GetString();
        }
        else if (first.TryGetProperty("id", out var idElem))
        {
            rawTvdbId = idElem.ToString();
        }
        else if (first.TryGetProperty("objectID", out var objIdElem))
        {
            rawTvdbId = objIdElem.GetString();
        }

        var numericId = ExtractNumericId(rawTvdbId);
        var parsedYear = year;
        if (parsedYear == 0 && first.TryGetProperty("year", out var yrElem))
        {
            if (yrElem.ValueKind == JsonValueKind.Number && yrElem.TryGetInt32(out var y))
            {
                parsedYear = y;
            }
            else if (yrElem.ValueKind == JsonValueKind.String && int.TryParse(yrElem.GetString(), out var sy))
            {
                parsedYear = sy;
            }
        }

        var meta = new MediaMetadata
        {
            Title = resultTitle,
            Year = parsedYear,
            MediaType = isMovie ? "Movie" : "TV",
            Overview = overview,
            Rating = rating,
            TvdbId = numericId ?? rawTvdbId,
        };

        if (first.TryGetProperty("image_url", out var img) && !string.IsNullOrWhiteSpace(img.GetString()))
        {
            meta.PosterUrl = NormalizeTvdbUrl(img.GetString());
        }

        // Extended details query
        if (!string.IsNullOrEmpty(numericId))
        {
            try
            {
                var typeSegment = isMovie ? "movies" : "series";
                var extendedEndpoint = $"https://api4.thetvdb.com/v4/{typeSegment}/{numericId}/extended";

                using var extReq = new HttpRequestMessage(HttpMethod.Get, extendedEndpoint);
                extReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                extReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var extResp = await this.httpClient.SendAsync(extReq);
                if (extResp.IsSuccessStatusCode)
                {
                    var extJson = await extResp.Content.ReadAsStringAsync();
                    using var extDoc = JsonDocument.Parse(extJson);
                    if (extDoc.RootElement.TryGetProperty("data", out var extData) && extData.ValueKind == JsonValueKind.Object)
                    {
                        if (extData.TryGetProperty("name", out var extName) && !string.IsNullOrWhiteSpace(extName.GetString()))
                        {
                            meta.Title = extName.GetString();
                        }

                        if (extData.TryGetProperty("overview", out var extOv) && !string.IsNullOrWhiteSpace(extOv.GetString()))
                        {
                            meta.Overview = extOv.GetString();
                        }

                        if (extData.TryGetProperty("score", out var extScore) && extScore.TryGetDouble(out var es))
                        {
                            meta.Rating = es;
                        }

                        if (extData.TryGetProperty("year", out var extYr) && int.TryParse(extYr.GetString(), out var ey) && ey > 0)
                        {
                            meta.Year = ey;
                        }

                        if (extData.TryGetProperty("image", out var extImg) && !string.IsNullOrWhiteSpace(extImg.GetString()))
                        {
                            meta.PosterUrl = NormalizeTvdbUrl(extImg.GetString());
                        }

                        if (extData.TryGetProperty("artworks", out var artworks) && artworks.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var art in artworks.EnumerateArray())
                            {
                                var artType = art.TryGetProperty("type", out var atElem) && atElem.TryGetInt32(out var at) ? at : 0;
                                var artImage = art.TryGetProperty("image", out var aiElem) ? aiElem.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(artImage))
                                {
                                    var fullUrl = NormalizeTvdbUrl(artImage);
                                    if (artType == 1 && string.IsNullOrEmpty(meta.BackdropUrl))
                                    {
                                        meta.BackdropUrl = fullUrl;
                                    }
                                    else if (artType == 2 && string.IsNullOrEmpty(meta.PosterUrl))
                                    {
                                        meta.PosterUrl = fullUrl;
                                    }
                                    else if ((artType == 3 || artType == 7) && string.IsNullOrEmpty(meta.BannerUrl))
                                    {
                                        meta.BannerUrl = fullUrl;
                                    }
                                }
                            }
                        }

                        if (extData.TryGetProperty("genres", out var gArray) && gArray.ValueKind == JsonValueKind.Array)
                        {
                            meta.Genres = string.Join(", ", gArray.EnumerateArray()
                                .Where(g => g.TryGetProperty("name", out var gn) && gn.ValueKind == JsonValueKind.String)
                                .Select(g => g.GetProperty("name").GetString()));
                        }

                        if (extData.TryGetProperty("characters", out var chars) && chars.ValueKind == JsonValueKind.Array)
                        {
                            meta.Cast = chars.EnumerateArray()
                                .Where(c => (c.TryGetProperty("personName", out var pn) && !string.IsNullOrWhiteSpace(pn.GetString())) ||
                                            (c.TryGetProperty("name", out var cn) && !string.IsNullOrWhiteSpace(cn.GetString())))
                                .Select(c => c.TryGetProperty("personName", out var pn) && !string.IsNullOrWhiteSpace(pn.GetString()) ? pn.GetString() : c.GetProperty("name").GetString())
                                .Distinct()
                                .Take(10)
                                .ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed fetching extended details from TheTVDB v4 API for id {0}", numericId);
            }
        }

        return meta;
    }

    private async Task<MediaMetadata> FetchFromServarrAsync(string title, bool isMovie, int year, string infoHash)
    {
        var preferredType = isMovie ? "Radarr" : "Sonarr";
        var connections = this.arrRepository.GetEnabled()
            .OrderByDescending(c => string.Equals(c.ArrType, preferredType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var conn in connections)
        {
            if (string.IsNullOrWhiteSpace(conn.Url))
            {
                continue;
            }

            var baseUrl = conn.Url.TrimEnd('/');
            var isRadarr = string.Equals(conn.ArrType, "Radarr", StringComparison.OrdinalIgnoreCase);

            var endpoint = isRadarr
                ? $"{baseUrl}/api/v3/movie/lookup?term={Uri.EscapeDataString(title)}"
                : $"{baseUrl}/api/v3/series/lookup?term={Uri.EscapeDataString(title)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(conn.ApiKey))
            {
                req.Headers.Add("X-Api-Key", conn.ApiKey);
            }

            using var resp = await this.httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                continue;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                continue;
            }

            var first = doc.RootElement[0];
            var resultTitle = first.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : title;
            var resultYear = first.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var yr) ? yr : year;
            var overview = first.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.String ? ov.GetString() : string.Empty;

            var meta = new MediaMetadata
            {
                Title = resultTitle,
                Year = resultYear,
                Overview = overview,
                MediaType = isRadarr ? "Movie" : "TV",
            };

            if (first.TryGetProperty("ratings", out var ratings) &&
                ServarrRatingsJsonParser.TryGetRating(ratings, out var servarrRating))
            {
                meta.Rating = servarrRating;
            }

            if (first.TryGetProperty("tvdbId", out var tvdb) && tvdb.ValueKind == JsonValueKind.Number && tvdb.TryGetInt32(out var tvdbInt) && tvdbInt > 0)
            {
                meta.TvdbId = tvdbInt.ToString();
            }

            if (first.TryGetProperty("imdbId", out var imdb) && imdb.ValueKind == JsonValueKind.String)
            {
                meta.ImdbId = imdb.GetString();
            }

            if (first.TryGetProperty("tmdbId", out var tmdb) && tmdb.ValueKind == JsonValueKind.Number && tmdb.TryGetInt32(out var tmdbInt) && tmdbInt > 0)
            {
                meta.TmdbId = tmdbInt.ToString();
            }

            if (first.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array)
            {
                meta.Genres = string.Join(", ", g.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
            }

            if (first.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in images.EnumerateArray())
                {
                    var coverType = img.TryGetProperty("coverType", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString() : string.Empty;
                    var url = img.TryGetProperty("remoteUrl", out var ru) && ru.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ru.GetString())
                        ? ru.GetString()
                        : (img.TryGetProperty("url", out var lu) && lu.ValueKind == JsonValueKind.String ? lu.GetString() : null);

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        if (url.StartsWith('/'))
                        {
                            url = $"{baseUrl}{url}";
                        }

                        if (coverType.Equals("poster", StringComparison.OrdinalIgnoreCase))
                        {
                            meta.PosterUrl = url;
                        }
                        else if (coverType.Equals("fanart", StringComparison.OrdinalIgnoreCase))
                        {
                            meta.BackdropUrl = url;
                        }
                        else if (coverType.Equals("banner", StringComparison.OrdinalIgnoreCase))
                        {
                            meta.BannerUrl = url;
                        }
                    }
                }
            }

            return meta;
        }

        return null;
    }

    private static string NormalizeTvdbUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return url.StartsWith('/') ? $"https://artworks.thetvdb.com{url}" : $"https://artworks.thetvdb.com/{url}";
    }

    private static string ExtractNumericId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return null;
        }

        var match = Regex.Match(rawId, @"\d+", RegexOptions.None, TimeSpan.FromSeconds(2));
        return match.Success ? match.Value : rawId;
    }

    private static string CleanTvTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(rawTitle, @"[._]", " ", RegexOptions.None, TimeSpan.FromSeconds(2));
        cleaned = Regex.Replace(cleaned, @"(?i)\b(S\d+(?:E\d+)?|\d+x\d+|Season\s*\d+|Episode\s*\d+|E\d{2,3})\b.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
        cleaned = Regex.Replace(cleaned, @"(?i)\b(1080p|720p|2160p|4k|uhd|hdr|remux|bluray|web-dl|webrip|x264|x265|hevc|h264|h265|dts|aac|repack|proper|internal|extended|unrated|multi|complete)\b.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
        cleaned = Regex.Replace(cleaned, @"(?<!^)\s*\b(19\d\d|20\d\d)\b.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
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
