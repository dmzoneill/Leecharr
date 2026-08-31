// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
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
    private readonly ConcurrentDictionary<string, byte> grabbedReleaseIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger logger;

    public RssSyncService(
        IIndexerRepository indexerRepository,
        IRssRuleRepository rssRuleRepository,
        ITorznabClient torznabClient,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser = null,
        HttpClient httpClient = null)
    {
        this.indexerRepository = indexerRepository;
        this.rssRuleRepository = rssRuleRepository;
        this.torznabClient = torznabClient;
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser ?? new TorrentFileParser();
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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
                        if (rule.IndexerIds.Count > 0 && !rule.IndexerIds.Contains(indexer.Id))
                        {
                            continue;
                        }

                        if (this.MatchesRule(release, rule))
                        {
                            this.logger.Info("RSS Rule '{0}' matched release: '{1}'. Grabbing...", rule.Name, release.Title);

                            var grabbed = false;
                            if (!string.IsNullOrEmpty(release.MagnetUrl))
                            {
                                await this.torrentService.AddFromMagnetAsync(release.MagnetUrl);
                                grabbed = true;
                            }
                            else if (!string.IsNullOrEmpty(release.DownloadUrl))
                            {
                                try
                                {
                                    var torrentBytes = await this.httpClient.GetByteArrayAsync(release.DownloadUrl);
                                    var parsed = this.torrentFileParser.Parse(torrentBytes);
                                    await this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, torrentBytes);
                                    grabbed = true;
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Error(ex, "Failed to download and add torrent file for release {0} from {1}", release.Title, release.DownloadUrl);
                                }
                            }

                            if (grabbed)
                            {
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
        if (rule.CategoryId > 0 && !string.IsNullOrWhiteSpace(release.Category))
        {
            var catStr = rule.CategoryId.ToString();
            if (!release.Category.Contains(catStr, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
