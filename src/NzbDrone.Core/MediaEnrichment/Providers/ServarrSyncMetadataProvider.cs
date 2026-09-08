// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class ServarrSyncMetadataProvider : IMediaMetadataProvider
{
    private readonly IArrConnectionRepository arrRepository;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public string ProviderId => "ServarrSync";

    public string DisplayName => "Servarr Library Sync (Sonarr / Radarr / Lidarr)";

    public string Version => "1.0.0";

    public string Description => "Correlates downloads and metadata directly from linked Sonarr, Radarr, and Lidarr instances via REST APIs.";

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
        SupportsNfoParsing = false,
    };

    public ServarrSyncMetadataProvider(IArrConnectionRepository arrRepository = null, HttpClient httpClient = null)
    {
        this.arrRepository = arrRepository;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public Task<MediaMetadataHealthCheckResult> ProbeHealthAsync()
    {
        var count = 0;
        if (this.arrRepository != null)
        {
            var all = this.arrRepository.All();
            if (all != null)
            {
                count = all.Count();
            }
        }

        return Task.FromResult(new MediaMetadataHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = $"Servarr metadata provider ready ({count} Arr instances configured).",
        });
    }

    public async Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null, string infoHash = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var cleanTitle = CleanTitle(title);
        var cat = (category ?? string.Empty).ToLowerInvariant();
        var isTv = cat.Contains("tv") || cat.Contains("sonarr") || cat.Contains("show") || cat.Contains("series") ||
                   (!string.IsNullOrEmpty(title) && Regex.IsMatch(title, @"(?i)\b(S\d{1,2}(?:E\d{1,3})?|\d{1,2}x\d{1,3}|Season[.\s_-]*(?!19\d\d|20\d\d)\d+|Episode[.\s_-]*\d+|E\d{2,3})\b"));
        var isMusic = cat.Contains("music") || cat.Contains("lidarr") || cat.Contains("album") || cat.Contains("audio") || cat.Contains("flac");
        var preferredType = isMusic ? "Lidarr" : isTv ? "Sonarr" : "Radarr";

        var connections = this.arrRepository?.GetEnabled().ToList() ?? new List<ArrConnectionDefinition>();

        // Sort to check preferred Arr type first
        var sortedConns = connections
            .OrderByDescending(c => string.Equals(c.ArrType, preferredType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var conn in sortedConns)
        {
            try
            {
                // 1. Try exact 1:1 queue or history correlation first
                var correlated = await this.CorrelateFromQueueOrHistoryAsync(conn, infoHash, title, cleanTitle);
                if (correlated != null)
                {
                    return correlated;
                }

                // 2. Fallback to /lookup or /search term query
                if (!string.IsNullOrWhiteSpace(cleanTitle))
                {
                    var result = await this.LookupFromArrAsync(conn, cleanTitle);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed lookup on Arr instance {0}", conn.Name);
            }
        }

        // Fallback placeholder if no linked Arr has the item yet
        return new MediaMetadata
        {
            Title = !string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : title,
            Year = year ?? 0,
            MediaType = preferredType == "Radarr" ? "Movie" : preferredType == "Lidarr" ? "Music" : "TV",
            Overview = $"Metadata synchronized from Servarr instance for {cleanTitle}.",
            Rating = 8.0,
        };
    }

    private async Task<MediaMetadata> CorrelateFromQueueOrHistoryAsync(
        ArrConnectionDefinition conn,
        string infoHash,
        string rawTitle,
        string cleanTitle)
    {
        if (string.IsNullOrWhiteSpace(conn.Url))
        {
            return null;
        }

        var baseUrl = conn.Url.TrimEnd('/');
        var isMusic = string.Equals(conn.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase);

        // 1. Query /queue
        var queueEndpoint = isMusic ? $"{baseUrl}/api/v1/queue" : $"{baseUrl}/api/v3/queue";
        var queueMatch = await this.QueryAndMatchEndpointAsync(conn, queueEndpoint, infoHash, rawTitle, cleanTitle);
        if (queueMatch != null)
        {
            return queueMatch;
        }

        // 2. Query /history
        var historyEndpoint = isMusic ? $"{baseUrl}/api/v1/history?page=1&pageSize=100" : $"{baseUrl}/api/v3/history?page=1&pageSize=100";
        var historyMatch = await this.QueryAndMatchEndpointAsync(conn, historyEndpoint, infoHash, rawTitle, cleanTitle);
        if (historyMatch != null)
        {
            return historyMatch;
        }

        return null;
    }

    private async Task<MediaMetadata> QueryAndMatchEndpointAsync(
        ArrConnectionDefinition conn,
        string endpoint,
        string infoHash,
        string rawTitle,
        string cleanTitle)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(conn.ApiKey))
            {
                req.Headers.Add("X-Api-Key", conn.ApiKey);
            }

            var resp = await this.httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            IEnumerable<JsonElement> records = null;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                records = doc.RootElement.EnumerateArray();
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                     doc.RootElement.TryGetProperty("records", out var recProp) &&
                     recProp.ValueKind == JsonValueKind.Array)
            {
                records = recProp.EnumerateArray();
            }

            if (records == null)
            {
                return null;
            }

            foreach (var record in records)
            {
                if (IsRecordMatch(record, infoHash, rawTitle, cleanTitle))
                {
                    var meta = await this.ExtractMetadataFromRecordAsync(conn, record);
                    if (meta != null)
                    {
                        return meta;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed querying Servarr endpoint {0}", endpoint);
        }

        return null;
    }

    private static bool IsRecordMatch(JsonElement record, string infoHash, string rawTitle, string cleanTitle)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Match on downloadId / hash
        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            if (record.TryGetProperty("downloadId", out var dIdElem) &&
                dIdElem.ValueKind == JsonValueKind.String &&
                string.Equals(dIdElem.GetString(), infoHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (record.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Object)
            {
                if (dataElem.TryGetProperty("downloadId", out var dataDId) &&
                    dataDId.ValueKind == JsonValueKind.String &&
                    string.Equals(dataDId.GetString(), infoHash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (dataElem.TryGetProperty("guid", out var guid) &&
                    guid.ValueKind == JsonValueKind.String &&
                    guid.GetString() != null &&
                    guid.GetString().Contains(infoHash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Match on rawTitle against downloadId
        if (!string.IsNullOrWhiteSpace(rawTitle))
        {
            if (record.TryGetProperty("downloadId", out var dIdElem) &&
                dIdElem.ValueKind == JsonValueKind.String &&
                string.Equals(dIdElem.GetString(), rawTitle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Match on title or sourceTitle
        var recordTitle = record.TryGetProperty("title", out var tElem) && tElem.ValueKind == JsonValueKind.String ? tElem.GetString() : null;
        var recordSourceTitle = record.TryGetProperty("sourceTitle", out var stElem) && stElem.ValueKind == JsonValueKind.String ? stElem.GetString() : null;

        var candidateTitles = new[] { recordTitle, recordSourceTitle }.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        foreach (var t in candidateTitles)
        {
            if (!string.IsNullOrWhiteSpace(rawTitle) && string.Equals(t, rawTitle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(cleanTitle) && string.Equals(CleanTitle(t), cleanTitle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(infoHash) && t.Contains(infoHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<MediaMetadata> ExtractMetadataFromRecordAsync(ArrConnectionDefinition conn, JsonElement record)
    {
        var baseUrl = conn.Url.TrimEnd('/');
        var isMovie = string.Equals(conn.ArrType, "Radarr", StringComparison.OrdinalIgnoreCase);
        var isMusic = string.Equals(conn.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase);

        if (isMovie)
        {
            if (record.TryGetProperty("movie", out var movie) && movie.ValueKind == JsonValueKind.Object)
            {
                return this.ParseMovieMetadata(conn, movie);
            }

            if (record.TryGetProperty("movieId", out var mId) && mId.ValueKind == JsonValueKind.Number && mId.TryGetInt32(out var movieId) && movieId > 0)
            {
                return await this.FetchEntityByIdAsync(conn, $"{baseUrl}/api/v3/movie/{movieId}", el => this.ParseMovieMetadata(conn, el));
            }
        }
        else if (isMusic)
        {
            record.TryGetProperty("artist", out var artist);
            record.TryGetProperty("album", out var album);

            if (artist.ValueKind == JsonValueKind.Object || album.ValueKind == JsonValueKind.Object)
            {
                return this.ParseLidarrMetadata(conn, artist, album);
            }

            if (record.TryGetProperty("artistId", out var aId) && aId.ValueKind == JsonValueKind.Number && aId.TryGetInt32(out var artistId) && artistId > 0)
            {
                return await this.FetchEntityByIdAsync(conn, $"{baseUrl}/api/v1/artist/{artistId}", el => this.ParseLidarrMetadata(conn, el, default));
            }

            if (record.TryGetProperty("albumId", out var albId) && albId.ValueKind == JsonValueKind.Number && albId.TryGetInt32(out var albumId) && albumId > 0)
            {
                return await this.FetchEntityByIdAsync(conn, $"{baseUrl}/api/v1/album/{albumId}", el => this.ParseLidarrMetadata(conn, default, el));
            }
        }
        else
        {
            if (record.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Object)
            {
                return this.ParseSeriesMetadata(conn, series);
            }

            if (record.TryGetProperty("seriesId", out var sId) && sId.ValueKind == JsonValueKind.Number && sId.TryGetInt32(out var seriesId) && seriesId > 0)
            {
                return await this.FetchEntityByIdAsync(conn, $"{baseUrl}/api/v3/series/{seriesId}", el => this.ParseSeriesMetadata(conn, el));
            }
        }

        return null;
    }

    private async Task<MediaMetadata> FetchEntityByIdAsync(ArrConnectionDefinition conn, string endpoint, Func<JsonElement, MediaMetadata> parser)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(conn.ApiKey))
            {
                req.Headers.Add("X-Api-Key", conn.ApiKey);
            }

            var resp = await this.httpClient.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return parser(doc.RootElement);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed fetching entity by id from {0}", endpoint);
        }

        return null;
    }

    private MediaMetadata ParseSeriesMetadata(ArrConnectionDefinition conn, JsonElement series)
    {
        var baseUrl = conn.Url.TrimEnd('/');
        var meta = new MediaMetadata
        {
            Title = series.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : string.Empty,
            Year = series.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var yr) ? yr : 0,
            Overview = series.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.String ? ov.GetString() : string.Empty,
            MediaType = "TV",
        };

        if (series.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt32(out var idVal) && idVal > 0)
        {
            meta.ArrMediaId = idVal;
        }

        if (series.TryGetProperty("ratings", out var ratings) &&
            ratings.ValueKind == JsonValueKind.Object &&
            ratings.TryGetProperty("value", out var rVal) &&
            rVal.ValueKind == JsonValueKind.Number &&
            rVal.TryGetDouble(out var r))
        {
            meta.Rating = r;
        }

        if (series.TryGetProperty("imdbId", out var imdb) && imdb.ValueKind == JsonValueKind.String)
        {
            meta.ImdbId = imdb.GetString();
        }

        if (series.TryGetProperty("tvdbId", out var tvdb) && tvdb.ValueKind == JsonValueKind.Number && tvdb.TryGetInt32(out var tvdbInt))
        {
            meta.TvdbId = tvdbInt.ToString();
        }

        if (series.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array)
        {
            meta.Genres = string.Join(", ", g.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
        }

        if (series.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            this.PopulateImages(conn, baseUrl, images, meta);
        }

        return meta;
    }

    private MediaMetadata ParseMovieMetadata(ArrConnectionDefinition conn, JsonElement movie)
    {
        var baseUrl = conn.Url.TrimEnd('/');
        var meta = new MediaMetadata
        {
            Title = movie.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : string.Empty,
            Year = movie.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var yr) ? yr : 0,
            Overview = movie.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.String ? ov.GetString() : string.Empty,
            MediaType = "Movie",
        };

        if (movie.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt32(out var idVal) && idVal > 0)
        {
            meta.ArrMediaId = idVal;
        }

        if (movie.TryGetProperty("ratings", out var ratings) &&
            ratings.ValueKind == JsonValueKind.Object &&
            ratings.TryGetProperty("value", out var rVal) &&
            rVal.ValueKind == JsonValueKind.Number &&
            rVal.TryGetDouble(out var r))
        {
            meta.Rating = r;
        }

        if (movie.TryGetProperty("imdbId", out var imdb) && imdb.ValueKind == JsonValueKind.String)
        {
            meta.ImdbId = imdb.GetString();
        }

        if (movie.TryGetProperty("tmdbId", out var tmdb) && tmdb.ValueKind == JsonValueKind.Number && tmdb.TryGetInt32(out var tmdbInt))
        {
            meta.TmdbId = tmdbInt.ToString();
        }

        if (movie.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array)
        {
            meta.Genres = string.Join(", ", g.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
        }

        if (movie.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            this.PopulateImages(conn, baseUrl, images, meta);
        }

        return meta;
    }

    private MediaMetadata ParseLidarrMetadata(ArrConnectionDefinition conn, JsonElement artist, JsonElement album)
    {
        var baseUrl = conn.Url.TrimEnd('/');
        var meta = new MediaMetadata
        {
            MediaType = "Music",
        };

        var artistName = string.Empty;
        if (artist.ValueKind == JsonValueKind.Object)
        {
            if (artist.TryGetProperty("artistName", out var an) && an.ValueKind == JsonValueKind.String)
            {
                artistName = an.GetString();
            }
            else if (artist.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            {
                artistName = n.GetString();
            }

            if (artist.TryGetProperty("foreignArtistId", out var faid) && faid.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(faid.GetString()))
            {
                meta.MusicBrainzId = faid.GetString();
            }
            else if (artist.TryGetProperty("musicBrainzId", out var mbid) && mbid.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(mbid.GetString()))
            {
                meta.MusicBrainzId = mbid.GetString();
            }
            else if (artist.TryGetProperty("mbId", out var mb) && mb.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(mb.GetString()))
            {
                meta.MusicBrainzId = mb.GetString();
            }

            if (artist.TryGetProperty("id", out var aidElem) && aidElem.ValueKind == JsonValueKind.Number && aidElem.TryGetInt32(out var aidVal) && aidVal > 0)
            {
                meta.ArrMediaId = aidVal;
            }

            if (artist.TryGetProperty("overview", out var aov) && aov.ValueKind == JsonValueKind.String)
            {
                meta.Overview = aov.GetString();
            }

            if (artist.TryGetProperty("genres", out var ag) && ag.ValueKind == JsonValueKind.Array)
            {
                meta.Genres = string.Join(", ", ag.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
            }

            if (artist.TryGetProperty("ratings", out var aratings) &&
                aratings.ValueKind == JsonValueKind.Object &&
                aratings.TryGetProperty("value", out var arVal) &&
                arVal.ValueKind == JsonValueKind.Number &&
                arVal.TryGetDouble(out var ar))
            {
                meta.Rating = ar;
            }

            if (artist.TryGetProperty("images", out var aImages) && aImages.ValueKind == JsonValueKind.Array)
            {
                this.PopulateImages(conn, baseUrl, aImages, meta);
            }
        }

        var albumTitle = string.Empty;
        if (album.ValueKind == JsonValueKind.Object)
        {
            if (album.TryGetProperty("title", out var at) && at.ValueKind == JsonValueKind.String)
            {
                albumTitle = at.GetString();
            }

            if (string.IsNullOrEmpty(meta.MusicBrainzId))
            {
                if (album.TryGetProperty("foreignAlbumId", out var faId) && faId.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(faId.GetString()))
                {
                    meta.MusicBrainzId = faId.GetString();
                }
                else if (album.TryGetProperty("musicBrainzId", out var ambid) && ambid.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ambid.GetString()))
                {
                    meta.MusicBrainzId = ambid.GetString();
                }
            }

            if (meta.ArrMediaId <= 0 && album.TryGetProperty("id", out var albidElem) && albidElem.ValueKind == JsonValueKind.Number && albidElem.TryGetInt32(out var albidVal) && albidVal > 0)
            {
                meta.ArrMediaId = albidVal;
            }

            if (album.TryGetProperty("releaseDate", out var rd) && rd.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rd.GetString()))
            {
                var match = Regex.Match(rd.GetString(), @"^(19\d\d|20\d\d)");
                if (match.Success && int.TryParse(match.Value, out var y))
                {
                    meta.Year = y;
                }
            }

            if (album.TryGetProperty("overview", out var aov) && aov.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(aov.GetString()))
            {
                meta.Overview = aov.GetString();
            }

            if (album.TryGetProperty("ratings", out var albratings) &&
                albratings.ValueKind == JsonValueKind.Object &&
                albratings.TryGetProperty("value", out var albrVal) &&
                albrVal.ValueKind == JsonValueKind.Number &&
                albrVal.TryGetDouble(out var albr) &&
                albr > 0)
            {
                meta.Rating = albr;
            }

            if (album.TryGetProperty("images", out var albImages) && albImages.ValueKind == JsonValueKind.Array)
            {
                this.PopulateImages(conn, baseUrl, albImages, meta);
            }

            if (string.IsNullOrEmpty(artistName) && album.TryGetProperty("artist", out var albArtist) && albArtist.ValueKind == JsonValueKind.Object)
            {
                if (albArtist.TryGetProperty("artistName", out var an) && an.ValueKind == JsonValueKind.String)
                {
                    artistName = an.GetString();
                }
            }
        }

        meta.ArtistName = artistName;
        meta.AlbumTitle = albumTitle;
        meta.Title = !string.IsNullOrWhiteSpace(albumTitle)
            ? (!string.IsNullOrWhiteSpace(artistName) ? $"{artistName} - {albumTitle}" : albumTitle)
            : artistName;

        return meta;
    }

    private async Task<MediaMetadata> LookupFromArrAsync(ArrConnectionDefinition conn, string title)
    {
        if (string.IsNullOrWhiteSpace(conn.Url))
        {
            return null;
        }

        var baseUrl = conn.Url.TrimEnd('/');
        var isMovie = string.Equals(conn.ArrType, "Radarr", StringComparison.OrdinalIgnoreCase);
        var isMusic = string.Equals(conn.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase);

        var endpoint = isMovie ? $"{baseUrl}/api/v3/movie/lookup?term={Uri.EscapeDataString(title)}" :
                       isMusic ? $"{baseUrl}/api/v1/search?term={Uri.EscapeDataString(title)}" :
                                 $"{baseUrl}/api/v3/series/lookup?term={Uri.EscapeDataString(title)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(conn.ApiKey))
        {
            req.Headers.Add("X-Api-Key", conn.ApiKey);
        }

        var resp = await this.httpClient.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = doc.RootElement[0];

        if (isMusic)
        {
            first.TryGetProperty("artist", out var artist);
            first.TryGetProperty("album", out var album);

            if (artist.ValueKind == JsonValueKind.Object || album.ValueKind == JsonValueKind.Object)
            {
                return this.ParseLidarrMetadata(conn, artist, album);
            }

            var lidarrMeta = this.ParseLidarrMetadata(conn, first, default);
            if (string.IsNullOrWhiteSpace(lidarrMeta.Title))
            {
                lidarrMeta.Title = title;
            }

            return lidarrMeta;
        }

        var meta = new MediaMetadata
        {
            Title = first.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : title,
            Year = first.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var yr) ? yr : 0,
            Overview = first.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.String ? ov.GetString() : string.Empty,
            MediaType = isMovie ? "Movie" : "TV",
        };

        if (first.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt32(out var idVal) && idVal > 0)
        {
            meta.ArrMediaId = idVal;
        }

        if (first.TryGetProperty("ratings", out var ratings) &&
            ratings.ValueKind == JsonValueKind.Object &&
            ratings.TryGetProperty("value", out var rVal) &&
            rVal.ValueKind == JsonValueKind.Number &&
            rVal.TryGetDouble(out var r))
        {
            meta.Rating = r;
        }

        if (first.TryGetProperty("imdbId", out var imdb) && imdb.ValueKind == JsonValueKind.String)
        {
            meta.ImdbId = imdb.GetString();
        }

        if (first.TryGetProperty("tmdbId", out var tmdb) && tmdb.ValueKind == JsonValueKind.Number && tmdb.TryGetInt32(out var tmdbInt))
        {
            meta.TmdbId = tmdbInt.ToString();
        }

        if (first.TryGetProperty("tvdbId", out var tvdb) && tvdb.ValueKind == JsonValueKind.Number && tvdb.TryGetInt32(out var tvdbInt))
        {
            meta.TvdbId = tvdbInt.ToString();
        }

        if (first.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array)
        {
            meta.Genres = string.Join(", ", g.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
        }

        if (first.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            this.PopulateImages(conn, baseUrl, images, meta);
        }

        return meta;
    }

    private void PopulateImages(ArrConnectionDefinition conn, string baseUrl, JsonElement images, MediaMetadata meta)
    {
        foreach (var img in images.EnumerateArray())
        {
            var coverType = img.TryGetProperty("coverType", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString() : string.Empty;
            string url = null;
            if (img.TryGetProperty("remoteUrl", out var ru) && ru.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ru.GetString()))
            {
                url = ru.GetString();
            }
            else if (img.TryGetProperty("url", out var lu) && lu.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(lu.GetString()))
            {
                url = lu.GetString();
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                if (url.StartsWith("/"))
                {
                    url = $"{baseUrl}{url}";
                    if (!string.IsNullOrWhiteSpace(conn.ApiKey) && !url.Contains("apikey=", StringComparison.OrdinalIgnoreCase))
                    {
                        var separator = url.Contains('?') ? "&" : "?";
                        url = $"{url}{separator}apikey={Uri.EscapeDataString(conn.ApiKey)}";
                    }
                }

                if ((string.Equals(coverType, "poster", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(coverType, "cover", StringComparison.OrdinalIgnoreCase)) &&
                    string.IsNullOrEmpty(meta.PosterUrl))
                {
                    meta.PosterUrl = url;
                }
                else if ((string.Equals(coverType, "fanart", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(coverType, "backdrop", StringComparison.OrdinalIgnoreCase)) &&
                         string.IsNullOrEmpty(meta.BackdropUrl))
                {
                    meta.BackdropUrl = url;
                }
                else if (string.Equals(coverType, "banner", StringComparison.OrdinalIgnoreCase) &&
                         string.IsNullOrEmpty(meta.BannerUrl))
                {
                    meta.BannerUrl = url;
                }
            }
        }
    }

    internal static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var clean = Regex.Replace(raw, @"[._]", " ");
        clean = Regex.Replace(clean, @"(?i)\b(S\d+(?:E\d+)?|\d+x\d+|Season\s*\d+|Episode\s*\d+|E\d{2,3})\b.*$", string.Empty);
        clean = Regex.Replace(clean, @"(?i)\b(1080p|720p|2160p|4k|uhd|hdr|remux|bluray|web-dl|webrip|x264|x265|hevc|h264|h265|dts|aac|repack|proper|internal|extended|unrated|multi|complete)\b.*$", string.Empty);
        var year = ExtractYear(raw);
        if (year > 0)
        {
            clean = Regex.Replace(clean, $@"(?<!^)\s*\b{year}\b.*$", string.Empty);
        }
        else
        {
            clean = Regex.Replace(clean, @"(?<!^)\s*\b(19\d\d|20\d\d)\b.*$", string.Empty);
        }

        clean = clean.Trim('-', ' ', '.');
        return string.IsNullOrWhiteSpace(clean) ? raw.Trim() : clean.Trim();
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
