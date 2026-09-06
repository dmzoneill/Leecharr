// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using NLog;
using NzbDrone.Core.Http.Transport;

namespace NzbDrone.Core.Indexers;

public interface ITorznabClient
{
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
        string searchType = null);

    Task<List<TorznabSearchResult>> FetchRssAsync(IndexerDefinition indexer, int limit = 50);

    List<TorznabSearchResult> ParseTorznabFeedXml(string xml, IndexerDefinition indexer);

    Task<TorznabCapabilities> FetchCapabilitiesAsync(IndexerDefinition indexer, System.Threading.CancellationToken cancellationToken = default);

    TorznabCapabilities ParseCapabilitiesXml(string xml);

    Task<TorznabTestResult> TestConnectionAsync(IndexerDefinition indexer, System.Threading.CancellationToken cancellationToken = default);
}

public class TorznabClient : ITorznabClient
{
    private static readonly XNamespace TorznabNs = "http://torznab.com/schemas/2015/feed";
    private static readonly XNamespace NewznabNs = "http://www.newznab.com/DTD/2010/feeds/attributes/";
    private readonly HttpClient httpClient;
    private readonly Logger logger;

    public TorznabClient(IHttpTransportEngine transportEngine = null, HttpClient httpClient = null)
    {
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
        : this(null, httpClient)
    {
    }

    public async Task<List<TorznabSearchResult>> SearchAsync(
        IndexerDefinition indexer,
        string query,
        int? categoryId = null,
        int limit = 50,
        int offset = 0,
        int? season = null,
        int? ep = null,
        string imdbId = null,
        string tmdbId = null,
        string searchType = null)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return new List<TorznabSearchResult>();
        }

        try
        {
            var uriBuilder = new UriBuilder(indexer.Url);
            var mode = !string.IsNullOrWhiteSpace(searchType)
                ? searchType
                : (season.HasValue || ep.HasValue ? "tvsearch" : (!string.IsNullOrWhiteSpace(imdbId) ? "movie" : "search"));
            var queryParams = $"t={mode}&limit={limit}&offset={offset}";

            if (!string.IsNullOrWhiteSpace(query))
            {
                queryParams += $"&q={Uri.EscapeDataString(query)}";
            }

            if (season.HasValue)
            {
                queryParams += $"&season={season.Value}";
            }

            if (ep.HasValue)
            {
                queryParams += $"&ep={ep.Value}";
            }

            if (!string.IsNullOrWhiteSpace(imdbId))
            {
                queryParams += $"&imdbid={Uri.EscapeDataString(imdbId)}";
            }

            if (!string.IsNullOrWhiteSpace(tmdbId))
            {
                queryParams += $"&tmdbid={Uri.EscapeDataString(tmdbId)}";
            }

            if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
            {
                queryParams += $"&apikey={Uri.EscapeDataString(indexer.ApiKey)}";
            }

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                queryParams += $"&cat={categoryId.Value}";
            }
            else if (indexer.Categories != null && indexer.Categories.Count > 0)
            {
                queryParams += $"&cat={string.Join(",", indexer.Categories)}";
            }

            MergeQueryParams(uriBuilder, queryParams);

            this.logger.Debug("Torznab querying: {0}", uriBuilder.Uri);

            var xml = await this.httpClient.GetStringAsync(uriBuilder.Uri);
            return this.ParseTorznabFeedXml(xml, indexer);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to search Torznab indexer: {0}", indexer.Name);
            return new List<TorznabSearchResult>();
        }
    }

    public async Task<List<TorznabSearchResult>> FetchRssAsync(IndexerDefinition indexer, int limit = 50)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return new List<TorznabSearchResult>();
        }

        try
        {
            var uriBuilder = new UriBuilder(indexer.Url);
            var queryParams = $"t=search&limit={limit}";

            if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
            {
                queryParams += $"&apikey={Uri.EscapeDataString(indexer.ApiKey)}";
            }

            if (indexer.Categories != null && indexer.Categories.Count > 0)
            {
                queryParams += $"&cat={string.Join(",", indexer.Categories)}";
            }

            MergeQueryParams(uriBuilder, queryParams);

            var xml = await this.httpClient.GetStringAsync(uriBuilder.Uri);
            return this.ParseTorznabFeedXml(xml, indexer);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to fetch RSS from Torznab indexer: {0}", indexer.Name);
            return new List<TorznabSearchResult>();
        }
    }

    public List<TorznabSearchResult> ParseTorznabFeedXml(string xml, IndexerDefinition indexer)
    {
        var results = new List<TorznabSearchResult>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return results;
        }

        var sanitizedXml = SanitizeXml(xml);

        try
        {
            XDocument doc;
            try
            {
                doc = XDocument.Parse(sanitizedXml);
            }
            catch (XmlException)
            {
                var escaped = Regex.Replace(sanitizedXml, @"&(?!amp;|lt;|gt;|quot;|apos;|#\d+;|#x[0-9a-fA-F]+;)", "&amp;");
                doc = XDocument.Parse(escaped);
            }

            var errorElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase));
            if (errorElem != null)
            {
                this.logger.Warn(
                    "Torznab indexer '{0}' returned error code {1}: {2}",
                    indexer?.Name ?? "Unknown",
                    errorElem.Attribute("code")?.Value ?? "unknown",
                    errorElem.Attribute("description")?.Value ?? errorElem.Value);
                return results;
            }

            var channel = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("channel", StringComparison.OrdinalIgnoreCase));
            var items = channel != null
                ? channel.Elements().Where(e => e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                : doc.Descendants().Where(e => e.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase));

            foreach (var item in items)
            {
                var title = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                var guid = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("guid", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                var link = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))?.Value
                    ?? item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))?.Attribute("href")?.Value
                    ?? string.Empty;
                var enclosure = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("enclosure", StringComparison.OrdinalIgnoreCase));
                var downloadUrl = enclosure?.Attribute("url")?.Value ?? link;

                var pubDateStr = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pubDate", StringComparison.OrdinalIgnoreCase)
                    || e.Name.LocalName.Equals("published", StringComparison.OrdinalIgnoreCase)
                    || e.Name.LocalName.Equals("updated", StringComparison.OrdinalIgnoreCase))?.Value;
                var publishDate = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(pubDateStr))
                {
                    if (DateTimeOffset.TryParse(pubDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDto))
                    {
                        publishDate = parsedDto.UtcDateTime;
                    }
                    else if (DateTime.TryParse(pubDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedPubDate))
                    {
                        publishDate = parsedPubDate;
                    }
                }

                long size = 0;
                if (enclosure != null && !string.IsNullOrWhiteSpace(enclosure.Attribute("length")?.Value))
                {
                    size = ParseLong(enclosure.Attribute("length")?.Value);
                }
                else
                {
                    var sizeElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("size", StringComparison.OrdinalIgnoreCase));
                    if (sizeElem != null)
                    {
                        size = ParseLong(sizeElem.Value);
                    }
                }

                var seeders = 0;
                var leechers = 0;
                var downloadVolumeFactor = 1.0;
                var uploadVolumeFactor = 1.0;
                var isFreeleechAttr = false;
                var infoHash = string.Empty;
                var magnetUrl = string.Empty;
                var category = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

                var freeleechElem = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("freeleech", StringComparison.OrdinalIgnoreCase)) ?? item.Element(TorznabNs + "freeleech");
                if (freeleechElem != null)
                {
                    var flVal = freeleechElem.Value?.Trim();
                    if (string.Equals(flVal, "1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(flVal, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        isFreeleechAttr = true;
                        downloadVolumeFactor = 0.0;
                    }
                }

                var attrElements = item.Elements(TorznabNs + "attr")
                    .Concat(item.Elements(NewznabNs + "attr"))
                    .Concat(item.Elements().Where(e => e.Name.LocalName.Equals("attr", StringComparison.OrdinalIgnoreCase)));

                foreach (var attr in attrElements.Distinct())
                {
                    var name = attr.Attribute("name")?.Value?.ToLowerInvariant();
                    var value = attr.Attribute("value")?.Value ?? attr.Value;

                    switch (name)
                    {
                        case "seeders":
                            seeders = ParseInt(value, seeders);
                            break;
                        case "peers":
                        case "leechers":
                            leechers = ParseInt(value, leechers);
                            break;
                        case "freeleech":
                            var trimmedVal = value?.Trim();
                            if (string.Equals(trimmedVal, "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(trimmedVal, "true", StringComparison.OrdinalIgnoreCase))
                            {
                                isFreeleechAttr = true;
                                downloadVolumeFactor = 0.0;
                            }

                            break;
                        case "downloadvolumefactor":
                            if (!isFreeleechAttr)
                            {
                                downloadVolumeFactor = ParseDouble(value, downloadVolumeFactor);
                            }

                            break;
                        case "uploadvolumefactor":
                            uploadVolumeFactor = ParseDouble(value, uploadVolumeFactor);
                            break;
                        case "infohash":
                            infoHash = value?.Trim() ?? string.Empty;
                            break;
                        case "magneturl":
                            magnetUrl = value?.Trim() ?? string.Empty;
                            break;
                        case "category":
                            if (string.IsNullOrWhiteSpace(category))
                            {
                                category = value?.Trim() ?? string.Empty;
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
                    DownloadVolumeFactor = downloadVolumeFactor,
                    UploadVolumeFactor = uploadVolumeFactor,
                    Category = category,
                    PublishDate = publishDate,
                    IndexerName = indexer?.Name ?? "Indexer",
                    IndexerId = indexer?.Id ?? 0,
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

        var match = Regex.Match(value, @"-?\d+(?:\.\d+)?");
        if (match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public async Task<TorznabCapabilities> FetchCapabilitiesAsync(IndexerDefinition indexer, System.Threading.CancellationToken cancellationToken = default)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url))
        {
            return new TorznabCapabilities();
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
            return this.ParseCapabilitiesXml(xml);
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
            xml = SanitizeXml(xml);
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (XmlException)
            {
                var fixedXml = Regex.Replace(xml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[0-9a-fA-F]+);)", "&amp;");
                doc = XDocument.Parse(fixedXml);
            }

            var capsElem = doc.Root;
            if (capsElem == null)
            {
                return capabilities;
            }

            // Limits
            var limitsElem = capsElem.Element("limits") ?? capsElem.Element("server");
            if (limitsElem != null)
            {
                var defaultAttr = limitsElem.Attribute("default")?.Value;
                var maxAttr = limitsElem.Attribute("max")?.Value;
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
            var searchingElem = capsElem.Element("searching");
            if (searchingElem != null)
            {
                var searchMode = searchingElem.Element("search");
                if (searchMode != null)
                {
                    capabilities.SupportsSearch = string.Equals(searchMode.Attribute("available")?.Value, "yes", StringComparison.OrdinalIgnoreCase);
                }

                var tvMode = searchingElem.Element("tv-search");
                if (tvMode != null)
                {
                    capabilities.SupportsTvSearch = string.Equals(tvMode.Attribute("available")?.Value, "yes", StringComparison.OrdinalIgnoreCase);
                    var tvParams = tvMode.Attribute("supportedParams")?.Value;
                    if (!string.IsNullOrEmpty(tvParams))
                    {
                        capabilities.SupportedTvParams = tvParams.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                    }
                }

                var movieMode = searchingElem.Element("movie-search");
                if (movieMode != null)
                {
                    capabilities.SupportsMovieSearch = string.Equals(movieMode.Attribute("available")?.Value, "yes", StringComparison.OrdinalIgnoreCase);
                    var movieParams = movieMode.Attribute("supportedParams")?.Value;
                    if (!string.IsNullOrEmpty(movieParams))
                    {
                        capabilities.SupportedMovieParams = movieParams.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                    }
                }

                var musicMode = searchingElem.Element("music-search");
                if (musicMode != null)
                {
                    capabilities.SupportsMusicSearch = string.Equals(musicMode.Attribute("available")?.Value, "yes", StringComparison.OrdinalIgnoreCase);
                    var musicParams = musicMode.Attribute("supportedParams")?.Value;
                    if (!string.IsNullOrEmpty(musicParams))
                    {
                        capabilities.SupportedMusicParams = musicParams.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                    }
                }
            }

            // Categories
            var categoriesElem = capsElem.Element("categories");
            if (categoriesElem != null)
            {
                foreach (var catElem in categoriesElem.Elements("category"))
                {
                    var idStr = catElem.Attribute("id")?.Value;
                    var name = catElem.Attribute("name")?.Value ?? string.Empty;
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

                    foreach (var subcatElem in catElem.Elements("subcat"))
                    {
                        var subIdStr = subcatElem.Attribute("id")?.Value;
                        var subName = subcatElem.Attribute("name")?.Value ?? string.Empty;
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

            if (capsResp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(capsContent))
            {
                if (capsContent.Contains("<error", StringComparison.OrdinalIgnoreCase))
                {
                    var doc = XDocument.Parse(capsContent);
                    var errorElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase));
                    var code = errorElem?.Attribute("code")?.Value ?? "unknown";
                    var desc = errorElem?.Attribute("description")?.Value ?? "Torznab error";
                    return TorznabTestResult.Fail($"Torznab error ({code}): {desc}");
                }

                if (capsContent.Contains("<caps", StringComparison.OrdinalIgnoreCase))
                {
                    var caps = this.ParseCapabilitiesXml(capsContent);
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

            if (!searchResp.IsSuccessStatusCode)
            {
                return TorznabTestResult.Fail($"HTTP {(int)searchResp.StatusCode} {searchResp.ReasonPhrase}");
            }

            var searchContent = await searchResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(searchContent))
            {
                return TorznabTestResult.Fail("Empty response received from indexer.");
            }

            if (searchContent.Contains("<error", StringComparison.OrdinalIgnoreCase))
            {
                var doc = XDocument.Parse(searchContent);
                var errorElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase));
                var code = errorElem?.Attribute("code")?.Value ?? "unknown";
                var desc = errorElem?.Attribute("description")?.Value ?? "Torznab error";
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
