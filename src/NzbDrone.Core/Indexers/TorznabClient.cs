// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;

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
}

public class TorznabClient : ITorznabClient
{
    private static readonly XNamespace TorznabNs = "http://torznab.com/schemas/2015/feed";
    private static readonly XNamespace NewznabNs = "http://www.newznab.com/DTD/2010/feeds/attributes/";
    private readonly HttpClient httpClient;
    private readonly Logger logger;

    public TorznabClient(HttpClient httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        this.logger = LogManager.GetCurrentClassLogger();
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

            uriBuilder.Query = string.IsNullOrEmpty(uriBuilder.Query)
                ? queryParams
                : uriBuilder.Query.TrimStart('?') + "&" + queryParams;

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

            uriBuilder.Query = string.IsNullOrEmpty(uriBuilder.Query)
                ? queryParams
                : uriBuilder.Query.TrimStart('?') + "&" + queryParams;

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

        try
        {
            var doc = XDocument.Parse(xml);
            var channel = doc.Root?.Element("channel");
            if (channel == null)
            {
                return results;
            }

            foreach (var item in channel.Elements("item"))
            {
                var title = item.Element("title")?.Value ?? string.Empty;
                var guid = item.Element("guid")?.Value ?? string.Empty;
                var link = item.Element("link")?.Value ?? string.Empty;
                var enclosure = item.Element("enclosure");
                var downloadUrl = enclosure?.Attribute("url")?.Value ?? link;

                var pubDateStr = item.Element("pubDate")?.Value ?? item.Element("published")?.Value ?? item.Element("updated")?.Value;
                var publishDate = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(pubDateStr) &&
                    DateTime.TryParse(pubDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedPubDate))
                {
                    publishDate = parsedPubDate;
                }

                long size = 0;
                if (enclosure != null && long.TryParse(enclosure.Attribute("length")?.Value, out var len))
                {
                    size = len;
                }
                else if (item.Element("size") != null && long.TryParse(item.Element("size")?.Value, out var sz))
                {
                    size = sz;
                }

                var seeders = 0;
                var leechers = 0;
                var downloadVolumeFactor = 1.0;
                var uploadVolumeFactor = 1.0;
                var infoHash = string.Empty;
                var magnetUrl = string.Empty;
                var category = item.Element("category")?.Value ?? string.Empty;

                var attrElements = item.Elements(TorznabNs + "attr")
                    .Concat(item.Elements(NewznabNs + "attr"))
                    .Concat(item.Elements().Where(e => e.Name.LocalName.Equals("attr", StringComparison.OrdinalIgnoreCase)));

                foreach (var attr in attrElements.Distinct())
                {
                    var name = attr.Attribute("name")?.Value?.ToLowerInvariant();
                    var value = attr.Attribute("value")?.Value;

                    switch (name)
                    {
                        case "seeders":
                            int.TryParse(value, out seeders);
                            break;
                        case "peers":
                        case "leechers":
                            int.TryParse(value, out leechers);
                            break;
                        case "downloadvolumefactor":
                            double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out downloadVolumeFactor);
                            break;
                        case "uploadvolumefactor":
                            double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out uploadVolumeFactor);
                            break;
                        case "infohash":
                            infoHash = value ?? string.Empty;
                            break;
                        case "magneturl":
                            magnetUrl = value ?? string.Empty;
                            break;
                        case "category":
                            if (string.IsNullOrWhiteSpace(category))
                            {
                                category = value ?? string.Empty;
                            }

                            break;
                        case "size":
                            if (size == 0 && long.TryParse(value, out var parsedSize))
                            {
                                size = parsedSize;
                            }

                            break;
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
}
