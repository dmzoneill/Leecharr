// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Indexers;

public interface ITorznabClient
{
    Task<List<TorznabSearchResult>> SearchAsync(
        IndexerDefinition indexer,
        TorznabSearchCriteria criteria,
        System.Threading.CancellationToken cancellationToken = default);

    Task<List<TorznabSearchResult>> SearchAsync(
        IndexerDefinition indexer,
        string query,
        int? categoryId = null,
        int limit = 50,
        int offset = 0,
        int? season = null,
        int? ep = null,
        string imdbId = null,
        string tmdbId = null,
        string searchType = null,
        string tvdbId = null,
        string rid = null,
        int? year = null,
        string artist = null,
        string album = null,
        string author = null,
        string isbn = null,
        System.Threading.CancellationToken cancellationToken = default);

    Task<List<TorznabSearchResult>> FetchRssAsync(IndexerDefinition indexer, int limit = 50, System.Threading.CancellationToken cancellationToken = default);

    List<TorznabSearchResult> ParseTorznabFeedXml(string xml, IndexerDefinition indexer = null);

    Task<TorznabCapabilities> FetchCapabilitiesAsync(IndexerDefinition indexer, System.Threading.CancellationToken cancellationToken = default);

    TorznabCapabilities ParseCapabilitiesXml(string xml);

    Task<TorznabTestResult> TestConnectionAsync(IndexerDefinition indexer, System.Threading.CancellationToken cancellationToken = default);
}

public class TorznabClient : ITorznabClient
{
    private static readonly XNamespace TorznabNs = "http://torznab.com/schemas/2015/feed";
    private static readonly XNamespace NewznabNs = "http://www.newznab.com/DTD/2010/feeds/attributes/";
    private static readonly Regex MagnetRegex = new(@"magnet:\?xt=urn:bt[im]h:[^\s""'<>`\]\[]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> TimeZoneOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        { "UTC", "+00:00" },
        { "UT", "+00:00" },
        { "GMT", "+00:00" },
        { "Z", "+00:00" },
        { "EST", "-05:00" },
        { "EDT", "-04:00" },
        { "CST", "-06:00" },
        { "CDT", "-05:00" },
        { "MST", "-07:00" },
        { "MDT", "-06:00" },
        { "PST", "-08:00" },
        { "PDT", "-07:00" },
        { "AKST", "-09:00" },
        { "AKDT", "-08:00" },
        { "HST", "-10:00" },
        { "HDT", "-09:00" },
        { "WET", "+00:00" },
        { "WEST", "+01:00" },
        { "CET", "+01:00" },
        { "CEST", "+02:00" },
        { "EET", "+02:00" },
        { "EEST", "+03:00" },
        { "MSK", "+03:00" },
        { "MSD", "+04:00" },
        { "BST", "+01:00" },
        { "IST", "+05:30" },
        { "JST", "+09:00" },
        { "KST", "+09:00" },
        { "HKT", "+08:00" },
        { "SGT", "+08:00" },
        { "AEST", "+10:00" },
        { "AEDT", "+11:00" },
        { "ACST", "+09:30" },
        { "ACDT", "+10:30" },
        { "AWST", "+08:00" },
        { "NZST", "+12:00" },
        { "NZDT", "+13:00" },
    };

    private static readonly ConcurrentDictionary<string, (TorznabCapabilities Caps, DateTime ExpiresAt)> CapabilitiesCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IConfigService configService;
    private readonly HttpClient httpClient;
    private readonly Logger logger;

    public static TimeSpan CapabilitiesTtl { get; set; } = TimeSpan.FromHours(24);

    public static void ClearCapabilitiesCache()
    {
        CapabilitiesCache.Clear();
    }

    public static void InvalidateCapabilities(string url, string apiKey = null)
    {
        var key = GetCapabilitiesCacheKey(url, apiKey);
        CapabilitiesCache.TryRemove(key, out _);
    }

    public TorznabClient(
        IHttpTransportEngine transportEngine = null,
        HttpClient httpClient = null,
        IConfigService configService = null)
    {
        this.configService = configService;

        if (httpClient != null)
        {
            this.httpClient = httpClient;
        }
        else if (transportEngine != null)
        {
            this.httpClient = new HttpClient(new DynamicHttpTransportHandler(transportEngine), disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(25),
            };
        }
        else
        {
            this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        }

        if (!this.httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            this.httpClient.DefaultRequestHeaders.Add("User-Agent", "Leecharr/1.0 (Torznab Client)");
        }

        this.logger = LogManager.GetCurrentClassLogger();
    }

    public TorznabClient(HttpClient httpClient)
        : this(null, httpClient, null)
    {
    }

    public TorznabClient(IConfigService configService)
        : this(null, null, configService)
    {
    }

    internal int ResolveEffectiveLimit(IndexerDefinition indexer, int requestedLimit, bool isRss = false)
    {
        var effectiveLimit = requestedLimit;

        if (effectiveLimit <= 0)
        {
            if (this.configService != null && this.configService.TorznabDefaultPageSize > 0)
            {
                effectiveLimit = this.configService.TorznabDefaultPageSize;
            }
            else
            {
                effectiveLimit = 50;
            }
        }

        if (indexer != null && !string.IsNullOrWhiteSpace(indexer.Url))
        {
            var cacheKey = GetCapabilitiesCacheKey(indexer.Url, indexer.ApiKey);
            if (CapabilitiesCache.TryGetValue(cacheKey, out var cached) && cached.Caps != null)
            {
                if (cached.Caps.MaxPageSize > 0 && effectiveLimit > cached.Caps.MaxPageSize)
                {
                    effectiveLimit = cached.Caps.MaxPageSize;
                }
            }
        }

        if (this.configService != null && this.configService.TorznabMaxPageSize > 0 && effectiveLimit > this.configService.TorznabMaxPageSize)
        {
            effectiveLimit = this.configService.TorznabMaxPageSize;
        }

        return Math.Max(1, effectiveLimit);
    }

    public Task<List<TorznabSearchResult>> SearchAsync(
        IndexerDefinition indexer,
        string query,
        int? categoryId = null,
        int limit = 50,
        int offset = 0,
        int? season = null,
        int? ep = null,
        string imdbId = null,
        string tmdbId = null,
        string searchType = null,
        string tvdbId = null,
        string rid = null,
        int? year = null,
        string artist = null,
        string album = null,
        string author = null,
        string isbn = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var criteria = new TorznabSearchCriteria
        {
            Query = query,
            CategoryId = categoryId,
            Limit = limit,
            Offset = offset,
            Season = season,
            Ep = ep,
            ImdbId = imdbId,
            TmdbId = tmdbId,
            SearchType = searchType,
            TvdbId = tvdbId,
            Rid = rid,
            Year = year,
            Artist = artist,
            Album = album,
            Author = author,
            Isbn = isbn,
        };

        return this.SearchAsync(indexer, criteria, cancellationToken);
    }

    public async Task<List<TorznabSearchResult>> SearchAsync(
        IndexerDefinition indexer,
        TorznabSearchCriteria criteria,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return new List<TorznabSearchResult>();
        }

        criteria ??= new TorznabSearchCriteria();

        try
        {
            var uriBuilder = new UriBuilder(indexer.Url);
            var mode = !string.IsNullOrWhiteSpace(criteria.SearchType)
                ? criteria.SearchType
                : (criteria.Season.HasValue || criteria.Ep.HasValue || !string.IsNullOrWhiteSpace(criteria.TvdbId) || !string.IsNullOrWhiteSpace(criteria.Rid)
                    ? "tvsearch"
                    : (!string.IsNullOrWhiteSpace(criteria.ImdbId) || !string.IsNullOrWhiteSpace(criteria.TmdbId)
                        ? "movie"
                        : (!string.IsNullOrWhiteSpace(criteria.Artist) || !string.IsNullOrWhiteSpace(criteria.Album)
                            ? "music"
                            : (!string.IsNullOrWhiteSpace(criteria.Author) || !string.IsNullOrWhiteSpace(criteria.Isbn)
                                ? "book"
                                : "search"))));

            var effectiveLimit = this.ResolveEffectiveLimit(indexer, criteria.Limit);
            var queryParams = $"t={mode}&limit={effectiveLimit}&offset={criteria.Offset}";

            if (!string.IsNullOrWhiteSpace(criteria.Query))
            {
                queryParams += $"&q={Uri.EscapeDataString(criteria.Query)}";
            }

            if (criteria.Season.HasValue)
            {
                queryParams += $"&season={criteria.Season.Value}";
            }

            if (criteria.Ep.HasValue)
            {
                queryParams += $"&ep={criteria.Ep.Value}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.ImdbId))
            {
                var normalizedImdb = Regex.Replace(criteria.ImdbId.Trim(), @"^tt", string.Empty, RegexOptions.IgnoreCase);
                queryParams += $"&imdbid={Uri.EscapeDataString(normalizedImdb)}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.TmdbId))
            {
                queryParams += $"&tmdbid={Uri.EscapeDataString(criteria.TmdbId.Trim())}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.TvdbId))
            {
                queryParams += $"&tvdbid={Uri.EscapeDataString(criteria.TvdbId.Trim())}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.Rid))
            {
                queryParams += $"&rid={Uri.EscapeDataString(criteria.Rid.Trim())}";
            }

            if (criteria.Year.HasValue)
            {
                queryParams += $"&year={criteria.Year.Value}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.Artist))
            {
                queryParams += $"&artist={Uri.EscapeDataString(criteria.Artist.Trim())}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.Album))
            {
                queryParams += $"&album={Uri.EscapeDataString(criteria.Album.Trim())}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.Author))
            {
                queryParams += $"&author={Uri.EscapeDataString(criteria.Author.Trim())}";
            }

            if (!string.IsNullOrWhiteSpace(criteria.Isbn))
            {
                queryParams += $"&isbn={Uri.EscapeDataString(criteria.Isbn.Trim())}";
            }

            if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
            {
                queryParams += $"&apikey={Uri.EscapeDataString(indexer.ApiKey)}";
            }

            if (criteria.CategoryId.HasValue && criteria.CategoryId.Value > 0)
            {
                queryParams += $"&cat={criteria.CategoryId.Value}";
            }
            else if (indexer.Categories != null && indexer.Categories.Count > 0)
            {
                queryParams += $"&cat={string.Join(",", indexer.Categories)}";
            }

            MergeQueryParams(uriBuilder, queryParams);

            this.logger.Debug("Torznab querying: {0}", uriBuilder.Uri);

            using var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
            using var response = await this.httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                this.logger.Warn("Torznab query failed for {0}: HTTP {1}", indexer.Name, response.StatusCode);
                return new List<TorznabSearchResult>();
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return this.ParseTorznabFeedXml(xml, indexer);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to search Torznab indexer: {0}", indexer.Name);
            return new List<TorznabSearchResult>();
        }
    }

    public async Task<List<TorznabSearchResult>> FetchRssAsync(IndexerDefinition indexer, int limit = 50, System.Threading.CancellationToken cancellationToken = default)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return new List<TorznabSearchResult>();
        }

        try
        {
            var uriBuilder = new UriBuilder(indexer.Url);
            var effectiveLimit = this.ResolveEffectiveLimit(indexer, limit, isRss: true);
            var queryParams = $"t=search&limit={effectiveLimit}";

            if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
            {
                queryParams += $"&apikey={Uri.EscapeDataString(indexer.ApiKey)}";
            }

            if (indexer.Categories != null && indexer.Categories.Count > 0)
            {
                queryParams += $"&cat={string.Join(",", indexer.Categories)}";
            }

            MergeQueryParams(uriBuilder, queryParams);

            using var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
            using var response = await this.httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                this.logger.Warn("Failed to fetch RSS from Torznab indexer: {0}: HTTP {1}", indexer.Name, response.StatusCode);
                return new List<TorznabSearchResult>();
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return this.ParseTorznabFeedXml(xml, indexer);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to fetch RSS from Torznab indexer: {0}", indexer.Name);
            return new List<TorznabSearchResult>();
        }
    }

    public List<TorznabSearchResult> ParseTorznabFeedXml(string xml, IndexerDefinition indexer = null)
    {
        var results = new List<TorznabSearchResult>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return results;
        }

        if (AntiBotChallengeDetector.IsChallenge(xml))
        {
            this.logger.Warn("Torznab XML response from indexer '{0}' is an AntiBot / Cloudflare challenge page.", indexer?.Name ?? "Unknown");
            return results;
        }

        try
        {
            var doc = SafeParseXml(xml);

            var errorElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase));
            if (errorElem != null)
            {
                this.logger.Warn(
                    "Torznab indexer '{0}' returned error code {1}: {2}",
                    indexer?.Name ?? "Unknown",
                    GetAttributeValue(errorElem, "code") ?? "unknown",
                    WebUtility.HtmlDecode(GetAttributeValue(errorElem, "description") ?? errorElem.Value));
                return results;
            }

            var channel = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("channel", StringComparison.OrdinalIgnoreCase));
            var items = channel != null
                ? channel.Elements().Where(e => e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                : doc.Descendants().Where(e => e.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase));

            foreach (var item in items)
            {
                var rawTitle = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                var title = WebUtility.HtmlDecode(rawTitle?.Trim() ?? string.Empty);

                var rawGuid = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("guid", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                var guid = WebUtility.HtmlDecode(rawGuid?.Trim() ?? string.Empty);

                var linkElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase));
                var rawLink = linkElem?.Value ?? GetAttributeValue(linkElem, "href") ?? string.Empty;
                var link = WebUtility.HtmlDecode(rawLink?.Trim() ?? string.Empty);

                var enclosure = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("enclosure", StringComparison.OrdinalIgnoreCase));
                var enclosureUrl = GetAttributeValue(enclosure, "url");
                var downloadUrl = !string.IsNullOrWhiteSpace(enclosureUrl) ? WebUtility.HtmlDecode(enclosureUrl.Trim()) : link;

                var rawDescription = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase))?.Value;
                var description = !string.IsNullOrWhiteSpace(rawDescription) ? WebUtility.HtmlDecode(rawDescription.Trim()) : string.Empty;

                var rawEncoded = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("encoded", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase))?.Value;
                var encodedContent = !string.IsNullOrWhiteSpace(rawEncoded) ? WebUtility.HtmlDecode(rawEncoded.Trim()) : string.Empty;

                var rawDetails = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("comments", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("details", StringComparison.OrdinalIgnoreCase))?.Value;
                var details = !string.IsNullOrWhiteSpace(rawDetails) ? WebUtility.HtmlDecode(rawDetails.Trim()) : string.Empty;

                var pubDateStr = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pubDate", StringComparison.OrdinalIgnoreCase)
                    || e.Name.LocalName.Equals("published", StringComparison.OrdinalIgnoreCase)
                    || e.Name.LocalName.Equals("updated", StringComparison.OrdinalIgnoreCase))?.Value;
                var publishDate = ParsePublishDate(pubDateStr);

                long size = 0;
                var enclosureLength = GetAttributeValue(enclosure, "length");
                if (enclosure != null && !string.IsNullOrWhiteSpace(enclosureLength))
                {
                    size = ParseLong(enclosureLength);
                }
                else
                {
                    var sizeElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("size", StringComparison.OrdinalIgnoreCase));
                    if (sizeElem != null)
                    {
                        size = ParseLong(sizeElem.Value);
                    }
                }

                int? rawSeeders = null;
                int? rawLeechers = null;
                int? rawPeers = null;
                var downloadVolumeFactor = 1.0;
                var uploadVolumeFactor = 1.0;
                var hasFreeleechFlag = false;
                double? explicitDownloadVolumeFactor = null;
                var infoHash = string.Empty;
                var magnetUrl = string.Empty;
                double? minimumRatio = null;
                long? minimumSeedTime = null;
                var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var seedersElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("seeders", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("numseeders", StringComparison.OrdinalIgnoreCase));
                if (seedersElem != null)
                {
                    rawSeeders = ParseInt(seedersElem.Value, 0);
                }

                var leechersElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("leechers", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("numleechers", StringComparison.OrdinalIgnoreCase));
                if (leechersElem != null)
                {
                    rawLeechers = ParseInt(leechersElem.Value, 0);
                }

                var peersElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("peers", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("numpeers", StringComparison.OrdinalIgnoreCase));
                if (peersElem != null)
                {
                    rawPeers = ParseInt(peersElem.Value, 0);
                }

                foreach (var catElem in item.Elements().Where(e => e.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase)))
                {
                    var catId = GetAttributeValue(catElem, "id")?.Trim() ?? GetAttributeValue(catElem, "domain")?.Trim();
                    if (!string.IsNullOrWhiteSpace(catId))
                    {
                        categories.Add(WebUtility.HtmlDecode(catId));
                    }

                    var catName = GetAttributeValue(catElem, "name")?.Trim();
                    if (!string.IsNullOrWhiteSpace(catName))
                    {
                        categories.Add(WebUtility.HtmlDecode(catName));
                    }

                    var catVal = catElem.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(catVal))
                    {
                        categories.Add(WebUtility.HtmlDecode(catVal));
                    }
                }

                var freeleechElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("freeleech", StringComparison.OrdinalIgnoreCase)) ?? item.Element(TorznabNs + "freeleech");
                if (freeleechElem != null)
                {
                    var flVal = freeleechElem.Value?.Trim();
                    if (string.Equals(flVal, "1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(flVal, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFreeleechFlag = true;
                    }
                }

                var attrElements = item.Elements(TorznabNs + "attr")
                    .Concat(item.Elements(NewznabNs + "attr"))
                    .Concat(item.Elements().Where(e => e.Name.LocalName.Equals("attr", StringComparison.OrdinalIgnoreCase)));

                foreach (var attr in attrElements.Distinct())
                {
                    var name = GetAttributeValue(attr, "name")?.ToLowerInvariant();
                    var value = GetAttributeValue(attr, "value") ?? attr.Value;

                    switch (name)
                    {
                        case "seeders":
                        case "numseeders":
                            rawSeeders = ParseInt(value, rawSeeders ?? 0);
                            break;
                        case "leechers":
                        case "numleechers":
                            rawLeechers = ParseInt(value, rawLeechers ?? 0);
                            break;
                        case "peers":
                        case "numpeers":
                            rawPeers = ParseInt(value, rawPeers ?? 0);
                            break;
                        case "minimumratio":
                            minimumRatio = ParseNullableDouble(value);
                            break;
                        case "minimumseedtime":
                            minimumSeedTime = ParseNullableLong(value);
                            break;
                        case "freeleech":
                            var trimmedVal = value?.Trim();
                            if (string.Equals(trimmedVal, "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(trimmedVal, "true", StringComparison.OrdinalIgnoreCase))
                            {
                                hasFreeleechFlag = true;
                            }

                            break;
                        case "downloadvolumefactor":
                            explicitDownloadVolumeFactor = ParseDouble(value, 1.0);
                            break;
                        case "uploadvolumefactor":
                            uploadVolumeFactor = ParseDouble(value, uploadVolumeFactor);
                            break;
                        case "infohash":
                            infoHash = MagnetLinkParser.NormalizeInfoHash(value);
                            break;
                        case "magneturl":
                            magnetUrl = value?.Trim() ?? string.Empty;
                            break;
                        case "cat":
                        case "category":
                            var catAttrVal = value?.Trim();
                            if (!string.IsNullOrWhiteSpace(catAttrVal))
                            {
                                var decoded = WebUtility.HtmlDecode(catAttrVal);
                                var splitCategories = decoded.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var cat in splitCategories)
                                {
                                    var trimmedCat = cat.Trim();
                                    if (!string.IsNullOrEmpty(trimmedCat) && !categories.Contains(trimmedCat))
                                    {
                                        categories.Add(trimmedCat);
                                    }
                                }
                            }

                            break;
                        case "size":
                            if (size == 0)
                            {
                                size = ParseLong(value, size);
                            }

                            break;
                    }
                }

                if (explicitDownloadVolumeFactor.HasValue)
                {
                    downloadVolumeFactor = explicitDownloadVolumeFactor.Value;
                }
                else if (hasFreeleechFlag)
                {
                    downloadVolumeFactor = 0.0;
                }

                int seeders;
                int leechers;
                int peers;

                if (rawSeeders.HasValue && rawLeechers.HasValue)
                {
                    seeders = Math.Max(0, rawSeeders.Value);
                    leechers = Math.Max(0, rawLeechers.Value);
                    peers = rawPeers.HasValue ? Math.Max(rawPeers.Value, seeders + leechers) : (seeders + leechers);
                }
                else if (rawSeeders.HasValue && rawPeers.HasValue)
                {
                    seeders = Math.Max(0, rawSeeders.Value);
                    leechers = Math.Max(0, rawPeers.Value - seeders);
                    peers = Math.Max(rawPeers.Value, seeders + leechers);
                }
                else if (rawLeechers.HasValue && rawPeers.HasValue)
                {
                    leechers = Math.Max(0, rawLeechers.Value);
                    seeders = Math.Max(0, rawPeers.Value - leechers);
                    peers = Math.Max(rawPeers.Value, seeders + leechers);
                }
                else if (rawSeeders.HasValue)
                {
                    seeders = Math.Max(0, rawSeeders.Value);
                    leechers = 0;
                    peers = seeders;
                }
                else if (rawLeechers.HasValue)
                {
                    seeders = 0;
                    leechers = Math.Max(0, rawLeechers.Value);
                    peers = leechers;
                }
                else if (rawPeers.HasValue)
                {
                    seeders = 0;
                    leechers = Math.Max(0, rawPeers.Value);
                    peers = rawPeers.Value;
                }
                else
                {
                    seeders = 0;
                    leechers = 0;
                    peers = 0;
                }

                if (string.IsNullOrWhiteSpace(magnetUrl))
                {
                    if (!string.IsNullOrEmpty(downloadUrl) && downloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        magnetUrl = downloadUrl;
                    }
                    else if (!string.IsNullOrEmpty(link) && link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        magnetUrl = link;
                    }
                    else
                    {
                        var extracted = ExtractMagnetUri(description, encodedContent, rawDescription, rawEncoded);
                        if (!string.IsNullOrWhiteSpace(extracted))
                        {
                            magnetUrl = extracted;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(infoHash) && !string.IsNullOrWhiteSpace(magnetUrl))
                {
                    try
                    {
                        var parsedMagnet = MagnetLinkParser.Parse(magnetUrl);
                        if (!string.IsNullOrWhiteSpace(parsedMagnet?.InfoHash))
                        {
                            infoHash = MagnetLinkParser.NormalizeInfoHash(parsedMagnet.InfoHash);
                        }
                    }
                    catch
                    {
                        // Ignore parse failure
                    }
                }
                else if (!string.IsNullOrWhiteSpace(infoHash))
                {
                    infoHash = MagnetLinkParser.NormalizeInfoHash(infoHash);
                }

                var result = new TorznabSearchResult
                {
                    Title = title,
                    Guid = guid,
                    DownloadUrl = downloadUrl,
                    MagnetUrl = magnetUrl,
                    InfoHash = infoHash,
                    Size = size,
                    Seeders = seeders,
                    Leechers = leechers,
                    Peers = peers,
                    DownloadVolumeFactor = downloadVolumeFactor,
                    UploadVolumeFactor = uploadVolumeFactor,
                    Category = string.Join(", ", categories),
                    PublishDate = publishDate,
                    IndexerName = indexer?.Name ?? "Indexer",
                    IndexerId = indexer?.Id ?? 0,
                    MinimumRatio = minimumRatio,
                    MinimumSeedTime = minimumSeedTime,
                    Description = description,
                    DetailsUrl = details,
                    Comments = details,
                };

                if (indexer != null && indexer.FreeleechOnly && !result.IsFreeleech)
                {
                    continue; // Skip non-freeleech if filter enabled
                }

                if (indexer != null && result.Seeders < indexer.MinSeeders)
                {
                    continue; // Skip releases below min seeders
                }

                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to parse Torznab XML feed.");
        }

        return results;
    }

    internal static DateTime ParsePublishDate(string pubDateStr)
    {
        if (string.IsNullOrWhiteSpace(pubDateStr))
        {
            return DateTime.UtcNow;
        }

        var trimmed = pubDateStr.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimestamp) && unixTimestamp > 0)
        {
            try
            {
                if (unixTimestamp > 100_000_000_000L)
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp).UtcDateTime;
                }

                return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
            }
            catch
            {
                // If out of range, fall through
            }
        }

        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out var directDto))
        {
            return directDto.UtcDateTime;
        }

        var normalized = Regex.Replace(
            trimmed,
            @"\b([A-Za-z]{1,5})\b",
            m => TimeZoneOffsets.TryGetValue(m.Value, out var offset) ? offset : m.Value);

        if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, styles, out var normalizedDto))
        {
            return normalizedDto.UtcDateTime;
        }

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, styles, out var parsedPubDate))
        {
            return DateTime.SpecifyKind(parsedPubDate, DateTimeKind.Utc);
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out var parsedOriginal))
        {
            return DateTime.SpecifyKind(parsedOriginal, DateTimeKind.Utc);
        }

        return DateTime.UtcNow;
    }

    internal static string ExtractMagnetUri(params string[] candidates)
    {
        if (candidates == null)
        {
            return string.Empty;
        }

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var decoded = WebUtility.HtmlDecode(candidate);
            var match = MagnetRegex.Match(decoded);
            if (match.Success)
            {
                return match.Value.Trim();
            }

            var rawMatch = MagnetRegex.Match(candidate);
            if (rawMatch.Success)
            {
                return WebUtility.HtmlDecode(rawMatch.Value).Trim();
            }
        }

        return string.Empty;
    }

    internal static string SanitizeXml(string xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return xml;
        }

        var sb = new StringBuilder(xml.Length);
        foreach (var c in xml)
        {
            if (c < 0x20 && c != '\t' && c != '\r' && c != '\n')
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    internal static XDocument SafeParseXml(string rawXml)
    {
        var sanitized = SanitizeXml(rawXml);
        try
        {
            return XDocument.Parse(sanitized);
        }
        catch (XmlException)
        {
            var escaped = Regex.Replace(sanitized, @"&(?!amp;|lt;|gt;|quot;|apos;|#\d+;|#x[0-9a-fA-F]+;)", "&amp;");
            return XDocument.Parse(escaped);
        }
    }

    internal static int ParseInt(string value, int defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var match = Regex.Match(value, @"-?\d+");
        if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    internal static long ParseLong(string value, long defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var match = Regex.Match(value, @"-?\d+");
        if (match.Success && long.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    internal static double ParseDouble(string value, double defaultValue = 1.0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var normalized = value.Trim().Replace(',', '.');
        var match = Regex.Match(normalized, @"-?\d+(?:\.\d+)?");
        if (match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    internal static double? ParseNullableDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace(',', '.');
        var match = Regex.Match(normalized, @"-?\d+(?:\.\d+)?");
        if (match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    internal static long? ParseNullableLong(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"-?\d+");
        if (match.Success && long.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    public async Task<TorznabCapabilities> FetchCapabilitiesAsync(IndexerDefinition indexer, System.Threading.CancellationToken cancellationToken = default)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return new TorznabCapabilities();
        }

        var cacheKey = GetCapabilitiesCacheKey(indexer.Url, indexer.ApiKey);
        if (CapabilitiesCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow && cached.Caps != null)
        {
            this.logger.Debug("Torznab capabilities cache hit for: {0}", indexer.Name ?? indexer.Url);
            return cached.Caps;
        }

        try
        {
            var uriBuilder = new UriBuilder(indexer.Url);
            var query = "t=caps";
            if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
            {
                query += $"&apikey={Uri.EscapeDataString(indexer.ApiKey)}";
            }

            MergeQueryParams(uriBuilder, query);

            this.logger.Debug("Fetching Torznab capabilities: {0}", uriBuilder.Uri);

            using var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
            using var response = await this.httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                this.logger.Warn("Failed to fetch capabilities from {0}: HTTP {1}", indexer.Name, response.StatusCode);
                return new TorznabCapabilities();
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var caps = this.ParseCapabilitiesXml(xml);
            CapabilitiesCache[cacheKey] = (caps, DateTime.UtcNow.Add(CapabilitiesTtl));
            return caps;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error fetching Torznab capabilities for: {0}", indexer.Name);
            return new TorznabCapabilities();
        }
    }

    public TorznabCapabilities ParseCapabilitiesXml(string xml)
    {
        var capabilities = new TorznabCapabilities();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return capabilities;
        }

        try
        {
            var doc = SafeParseXml(xml);

            var root = doc.Root;
            if (root == null)
            {
                return capabilities;
            }

            var capsElem = root.Name.LocalName.Equals("caps", StringComparison.OrdinalIgnoreCase)
                ? root
                : (FindDescendant(root, "caps") ?? root);

            // Limits
            var limitsElem = FindElement(capsElem, "limits") ?? FindElement(capsElem, "server")
                ?? FindDescendant(capsElem, "limits") ?? FindDescendant(capsElem, "server");
            if (limitsElem != null)
            {
                var defaultAttr = GetAttributeValue(limitsElem, "default") ?? FindElement(limitsElem, "default")?.Value;
                var maxAttr = GetAttributeValue(limitsElem, "max") ?? FindElement(limitsElem, "max")?.Value;
                if (!string.IsNullOrEmpty(defaultAttr))
                {
                    capabilities.DefaultPageSize = ParseInt(defaultAttr, capabilities.DefaultPageSize);
                }

                if (!string.IsNullOrEmpty(maxAttr))
                {
                    capabilities.MaxPageSize = ParseInt(maxAttr, capabilities.MaxPageSize);
                }
            }

            // Searching modes
            var searchingElem = FindElement(capsElem, "searching") ?? FindDescendant(capsElem, "searching");
            if (searchingElem != null)
            {
                var searchMode = FindElement(searchingElem, "search");
                if (searchMode != null)
                {
                    capabilities.SupportsSearch = IsAvailable(searchMode);
                }

                var tvMode = FindElement(searchingElem, "tv-search") ?? FindElement(searchingElem, "tvsearch");
                if (tvMode != null)
                {
                    capabilities.SupportsTvSearch = IsAvailable(tvMode);
                    var tvParams = ParseSupportedParams(tvMode);
                    if (tvParams.Count > 0)
                    {
                        capabilities.SupportedTvParams = tvParams;
                    }
                }

                var movieMode = FindElement(searchingElem, "movie-search") ?? FindElement(searchingElem, "moviesearch");
                if (movieMode != null)
                {
                    capabilities.SupportsMovieSearch = IsAvailable(movieMode);
                    var movieParams = ParseSupportedParams(movieMode);
                    if (movieParams.Count > 0)
                    {
                        capabilities.SupportedMovieParams = movieParams;
                    }
                }

                var musicMode = FindElement(searchingElem, "music-search") ?? FindElement(searchingElem, "musicsearch")
                    ?? FindElement(searchingElem, "audio-search") ?? FindElement(searchingElem, "audiosearch");
                if (musicMode != null)
                {
                    capabilities.SupportsMusicSearch = IsAvailable(musicMode);
                    var musicParams = ParseSupportedParams(musicMode);
                    if (musicParams.Count > 0)
                    {
                        capabilities.SupportedMusicParams = musicParams;
                    }
                }

                var bookMode = FindElement(searchingElem, "book-search") ?? FindElement(searchingElem, "booksearch");
                if (bookMode != null)
                {
                    capabilities.SupportsBookSearch = IsAvailable(bookMode);
                    var bookParams = ParseSupportedParams(bookMode);
                    if (bookParams.Count > 0)
                    {
                        capabilities.SupportedBookParams = bookParams;
                    }
                }
            }

            // Categories
            var categoriesElem = FindElement(capsElem, "categories") ?? FindDescendant(capsElem, "categories");
            if (categoriesElem != null)
            {
                foreach (var catElem in FindElements(categoriesElem, "category"))
                {
                    var idStr = GetAttributeValue(catElem, "id") ?? FindElement(catElem, "id")?.Value;
                    var name = WebUtility.HtmlDecode(GetAttributeValue(catElem, "name") ?? FindElement(catElem, "name")?.Value ?? string.Empty);
                    var id = ParseInt(idStr, -1);
                    if (id < 0)
                    {
                        continue;
                    }

                    var cat = new TorznabCategory
                    {
                        Id = id,
                        Name = name,
                    };

                    var subcats = FindElements(catElem, "subcat").Concat(FindElements(catElem, "subcategory"));
                    foreach (var subcatElem in subcats)
                    {
                        var subIdStr = GetAttributeValue(subcatElem, "id") ?? FindElement(subcatElem, "id")?.Value;
                        var subName = WebUtility.HtmlDecode(GetAttributeValue(subcatElem, "name") ?? FindElement(subcatElem, "name")?.Value ?? string.Empty);
                        var subId = ParseInt(subIdStr, -1);
                        if (subId >= 0)
                        {
                            cat.SubCategories.Add(new TorznabCategory
                            {
                                Id = subId,
                                Name = subName,
                            });
                        }
                    }

                    capabilities.Categories.Add(cat);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to parse Torznab capabilities XML.");
        }

        return capabilities;
    }

    public async Task<TorznabTestResult> TestConnectionAsync(IndexerDefinition indexer, System.Threading.CancellationToken cancellationToken = default)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return TorznabTestResult.Fail("Indexer URL is empty.");
        }

        try
        {
            // 1. First attempt: t=caps
            var capsUriBuilder = new UriBuilder(indexer.Url);
            var capsQuery = "t=caps";
            if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
            {
                capsQuery += $"&apikey={Uri.EscapeDataString(indexer.ApiKey)}";
            }

            MergeQueryParams(capsUriBuilder, capsQuery);

            using var capsReq = new HttpRequestMessage(HttpMethod.Get, capsUriBuilder.Uri);
            var capsResp = await this.httpClient.SendAsync(capsReq, cancellationToken).ConfigureAwait(false);
            var capsContent = await capsResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (AntiBotChallengeDetector.IsChallenge(capsResp.StatusCode, capsContent, capsResp))
            {
                return TorznabTestResult.Fail("Cloudflare / AntiBot challenge detected. FlareSolverr may be required.");
            }

            if (capsResp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(capsContent))
            {
                if (capsContent.Contains("<error", StringComparison.OrdinalIgnoreCase))
                {
                    var doc = SafeParseXml(capsContent);
                    var errorElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase));
                    var code = GetAttributeValue(errorElem, "code") ?? "unknown";
                    var desc = WebUtility.HtmlDecode(GetAttributeValue(errorElem, "description") ?? "Torznab error");
                    return TorznabTestResult.Fail($"Torznab error ({code}): {desc}");
                }

                if (capsContent.Contains("<caps", StringComparison.OrdinalIgnoreCase))
                {
                    var caps = this.ParseCapabilitiesXml(capsContent);
                    var cacheKey = GetCapabilitiesCacheKey(indexer.Url, indexer.ApiKey);
                    CapabilitiesCache[cacheKey] = (caps, DateTime.UtcNow.Add(CapabilitiesTtl));
                    return TorznabTestResult.Ok(caps);
                }
            }

            // 2. Fallback attempt: t=search with limit=1 to verify endpoint & API key
            var searchUriBuilder = new UriBuilder(indexer.Url);
            var searchQuery = "t=search&limit=1";
            if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
            {
                searchQuery += $"&apikey={Uri.EscapeDataString(indexer.ApiKey)}";
            }

            MergeQueryParams(searchUriBuilder, searchQuery);

            using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUriBuilder.Uri);
            var searchResp = await this.httpClient.SendAsync(searchReq, cancellationToken).ConfigureAwait(false);

            var searchContent = await searchResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (AntiBotChallengeDetector.IsChallenge(searchResp.StatusCode, searchContent, searchResp))
            {
                return TorznabTestResult.Fail("Cloudflare / AntiBot challenge detected. FlareSolverr may be required.");
            }

            if (!searchResp.IsSuccessStatusCode)
            {
                return TorznabTestResult.Fail($"HTTP {(int)searchResp.StatusCode} {searchResp.ReasonPhrase}");
            }

            if (string.IsNullOrWhiteSpace(searchContent))
            {
                return TorznabTestResult.Fail("Empty response received from indexer.");
            }

            if (searchContent.Contains("<error", StringComparison.OrdinalIgnoreCase))
            {
                var doc = SafeParseXml(searchContent);
                var errorElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase));
                var code = GetAttributeValue(errorElem, "code") ?? "unknown";
                var desc = WebUtility.HtmlDecode(GetAttributeValue(errorElem, "description") ?? "Torznab error");
                return TorznabTestResult.Fail($"Torznab error ({code}): {desc}");
            }

            if (searchContent.Contains("<rss", StringComparison.OrdinalIgnoreCase) || searchContent.Contains("<channel", StringComparison.OrdinalIgnoreCase) || searchContent.Contains("<feed", StringComparison.OrdinalIgnoreCase))
            {
                return TorznabTestResult.Ok();
            }

            return TorznabTestResult.Fail("Response is not a valid Torznab XML feed.");
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Torznab connection test failed for {0} ({1})", indexer.Name, indexer.Url);
            return TorznabTestResult.Fail(ex.Message);
        }
    }

    internal static string GetCapabilitiesCacheKey(string url, string apiKey)
    {
        var cleanUrl = (url ?? string.Empty).Trim().TrimEnd('/');
        var cleanKey = (apiKey ?? string.Empty).Trim();
        return $"{cleanUrl}|{cleanKey}";
    }

    internal static XAttribute FindAttribute(XElement elem, string localName)
    {
        if (elem == null)
        {
            return null;
        }

        return elem.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string GetAttributeValue(XElement elem, string localName)
    {
        return FindAttribute(elem, localName)?.Value;
    }

    private static XElement FindElement(XContainer container, string localName)
    {
        return container?.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement FindDescendant(XContainer container, string localName)
    {
        return container?.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<XElement> FindElements(XContainer container, string localName)
    {
        if (container == null)
        {
            return Enumerable.Empty<XElement>();
        }

        return container.Elements().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAvailable(XElement elem)
    {
        if (elem == null)
        {
            return false;
        }

        var val = GetAttributeValue(elem, "available");
        return string.Equals(val, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(val, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseSupportedParams(XElement elem)
    {
        var val = GetAttributeValue(elem, "supportedParams") ?? FindElement(elem, "supportedParams")?.Value;
        if (string.IsNullOrWhiteSpace(val))
        {
            return new List<string>();
        }

        return val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static void MergeQueryParams(UriBuilder uriBuilder, string queryParams)
    {
        var existingParams = HttpUtility.ParseQueryString(uriBuilder.Query);
        var newParams = HttpUtility.ParseQueryString(queryParams);

        foreach (string key in newParams.AllKeys)
        {
            if (key != null)
            {
                existingParams[key] = newParams[key];
            }
        }

        uriBuilder.Query = existingParams.ToString();
    }
}
