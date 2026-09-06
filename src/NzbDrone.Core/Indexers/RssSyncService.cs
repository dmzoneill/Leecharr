// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Indexers;

public interface IRssSyncService
{
    Task<int> SyncRssFeedsAsync();

    bool MatchesRule(TorznabSearchResult release, RssRule rule);
}

public class RssSyncService : IRssSyncService
{
    private readonly IIndexerRepository indexerRepository;
    private readonly IRssRuleRepository rssRuleRepository;
    private readonly ITorznabClient torznabClient;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly HttpClient httpClient;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IDownloadHistoryService downloadHistoryService;
    private readonly ICategoryService categoryService;
    private readonly ConcurrentDictionary<string, byte> grabbedReleaseIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger logger;

    public RssSyncService(
        IIndexerRepository indexerRepository,
        IRssRuleRepository rssRuleRepository,
        ITorznabClient torznabClient,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser = null,
        HttpClient httpClient = null,
        ISafeHttpClientService safeHttpClientService = null,
        IDownloadHistoryService downloadHistoryService = null,
        ICategoryService categoryService = null)
    {
        this.indexerRepository = indexerRepository;
        this.rssRuleRepository = rssRuleRepository;
        this.torznabClient = torznabClient;
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser ?? new TorrentFileParser();
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        this.safeHttpClientService = safeHttpClientService ?? (httpClient != null ? new SafeHttpClientService(httpClient) : new SafeHttpClientService());
        this.downloadHistoryService = downloadHistoryService;
        this.categoryService = categoryService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<int> SyncRssFeedsAsync()
    {
        var activeIndexers = this.indexerRepository.GetRssEnabled().ToList();
        var activeRules = this.rssRuleRepository.GetEnabled().ToList();

        if (activeIndexers.Count == 0 || activeRules.Count == 0)
        {
            return 0;
        }

        var grabbedCount = 0;

        foreach (var indexer in activeIndexers)
        {
            try
            {
                var releases = await this.torznabClient.FetchRssAsync(indexer);
                foreach (var release in releases)
                {
                    var releaseId = GetReleaseId(release);
                    if (!string.IsNullOrEmpty(releaseId) && this.grabbedReleaseIds.ContainsKey(releaseId))
                    {
                        continue;
                    }

                    foreach (var rule in activeRules)
                    {
                        if (rule.IndexerIds != null && rule.IndexerIds.Count > 0 && !rule.IndexerIds.Contains(indexer.Id))
                        {
                            continue;
                        }

                        if (this.MatchesRule(release, rule))
                        {
                            this.logger.Info("RSS Rule '{0}' matched release: '{1}'. Grabbing...", rule.Name, release.Title);

                            string categoryName = null;
                            string savePath = null;
                            if (rule.CategoryId > 0 && this.categoryService != null)
                            {
                                var cat = this.categoryService.Get(rule.CategoryId);
                                categoryName = cat?.Name;
                                savePath = cat?.SavePath;
                            }

                            Torrent addedTorrent = null;
                            var grabbed = false;
                            try
                            {
                                if (!string.IsNullOrEmpty(release.MagnetUrl))
                                {
                                    addedTorrent = await this.torrentService.AddFromMagnetAsync(release.MagnetUrl, categoryName, savePath);
                                    grabbed = true;
                                }
                                else if (!string.IsNullOrEmpty(release.DownloadUrl))
                                {
                                    if (release.DownloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                                    {
                                        addedTorrent = await this.torrentService.AddFromMagnetAsync(release.DownloadUrl, categoryName, savePath);
                                        grabbed = true;
                                    }
                                    else
                                    {
                                        var torrentBytes = await this.safeHttpClientService.DownloadBytesAsync(release.DownloadUrl, maxSizeBytes: 10 * 1024 * 1024);
                                        var parsed = this.torrentFileParser.Parse(torrentBytes);
                                        addedTorrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, categoryName, savePath, false, torrentBytes);
                                        grabbed = true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                this.logger.Error(ex, "Failed to grab release {0}", release.Title);
                            }

                            if (grabbed)
                            {
                                var effectiveMagnet = !string.IsNullOrWhiteSpace(release.MagnetUrl)
                                    ? release.MagnetUrl
                                    : (release.DownloadUrl?.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) == true ? release.DownloadUrl : null);

                                this.downloadHistoryService?.RecordTorrentAdded(
                                    addedTorrent,
                                    source: $"RSS: {rule.Name}",
                                    magnetUrl: effectiveMagnet,
                                    downloadUrl: release.DownloadUrl,
                                    indexerName: indexer.Name);

                                if (!string.IsNullOrEmpty(releaseId))
                                {
                                    this.grabbedReleaseIds.TryAdd(releaseId, 0);
                                }

                                grabbedCount++;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error syncing RSS for indexer: {0}", indexer.Name);
            }
        }

        return grabbedCount;
    }

    private static string GetReleaseId(TorznabSearchResult release)
    {
        if (release == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(release.Guid))
        {
            return release.Guid;
        }

        if (!string.IsNullOrWhiteSpace(release.InfoHash))
        {
            return release.InfoHash;
        }

        if (!string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            return release.DownloadUrl;
        }

        if (!string.IsNullOrWhiteSpace(release.MagnetUrl))
        {
            return release.MagnetUrl;
        }

        return release.Title;
    }

    public bool MatchesRule(TorznabSearchResult release, RssRule rule)
    {
        if (release == null || rule == null || !rule.IsEnabled)
        {
            return false;
        }

        // 1. MustContain Regex
        if (!string.IsNullOrWhiteSpace(rule.MustContain))
        {
            try
            {
                if (!Regex.IsMatch(release.Title, rule.MustContain, RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // 2. MustNotContain Regex
        if (!string.IsNullOrWhiteSpace(rule.MustNotContain))
        {
            try
            {
                if (Regex.IsMatch(release.Title, rule.MustNotContain, RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // 3. MinSeeders
        if (release.Seeders < rule.MinSeeders)
        {
            return false;
        }

        // 4. MinSizeBytes
        if (rule.MinSizeBytes > 0 && release.Size < rule.MinSizeBytes)
        {
            return false;
        }

        // 5. MaxSizeBytes
        if (rule.MaxSizeBytes > 0 && release.Size > rule.MaxSizeBytes)
        {
            return false;
        }

        // 6. FreeleechOnly
        if (rule.FreeleechOnly && !release.IsFreeleech)
        {
            return false;
        }

        // 7. CategoryId matching
        if (rule.CategoryId > 0)
        {
            if (string.IsNullOrWhiteSpace(release.Category))
            {
                return false;
            }

            var catStr = rule.CategoryId.ToString();
            var tokens = release.Category.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!tokens.Any(t => string.Equals(t.Trim(), catStr, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }
}
