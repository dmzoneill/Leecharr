using System;
using System.Linq;
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
    private readonly IIndexerRepository _indexerRepository;
    private readonly IRssRuleRepository _rssRuleRepository;
    private readonly ITorznabClient _torznabClient;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public RssSyncService(
        IIndexerRepository indexerRepository,
        IRssRuleRepository rssRuleRepository,
        ITorznabClient torznabClient,
        ITorrentService torrentService)
    {
        _indexerRepository = indexerRepository;
        _rssRuleRepository = rssRuleRepository;
        _torznabClient = torznabClient;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<int> SyncRssFeedsAsync()
    {
        var activeIndexers = _indexerRepository.GetRssEnabled().ToList();
        var activeRules = _rssRuleRepository.GetEnabled().ToList();

        if (activeIndexers.Count == 0 || activeRules.Count == 0)
        {
            return 0;
        }

        var grabbedCount = 0;

        foreach (var indexer in activeIndexers)
        {
            try
            {
                var releases = await _torznabClient.FetchRssAsync(indexer);
                foreach (var release in releases)
                {
                    foreach (var rule in activeRules)
                    {
                        if (rule.IndexerIds.Count > 0 && !rule.IndexerIds.Contains(indexer.Id))
                        {
                            continue;
                        }

                        if (MatchesRule(release, rule))
                        {
                            _logger.Info("RSS Rule '{0}' matched release: '{1}'. Grabbing...", rule.Name, release.Title);

                            if (!string.IsNullOrEmpty(release.MagnetUrl))
                            {
                                await _torrentService.AddFromMagnetAsync(release.MagnetUrl);
                                grabbedCount++;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error syncing RSS for indexer: {0}", indexer.Name);
            }
        }

        return grabbedCount;
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

        return true;
    }
}
