// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
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
    private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IIndexerRepository indexerRepository;
    private readonly IRssRuleRepository rssRuleRepository;
    private readonly ITorznabClient torznabClient;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly HttpClient httpClient;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IDownloadHistoryService downloadHistoryService;
    private readonly ICategoryService categoryService;
    private readonly BoundedSet<string> grabbedReleaseIds = new(10000, StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim syncLock = new(1, 1);
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
        if (!this.syncLock.Wait(0))
        {
            this.logger.Warn("RSS sync is already in progress. Skipping duplicate execution.");
            return 0;
        }

        try
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
                        if (release != null)
                        {
                            if (!string.IsNullOrWhiteSpace(release.InfoHash))
                            {
                                release.InfoHash = MagnetLinkParser.NormalizeInfoHash(release.InfoHash);
                            }
                            else if (!string.IsNullOrWhiteSpace(release.MagnetUrl))
                            {
                                try
                                {
                                    var parsed = MagnetLinkParser.Parse(release.MagnetUrl);
                                    if (!string.IsNullOrWhiteSpace(parsed?.InfoHash))
                                    {
                                        release.InfoHash = MagnetLinkParser.NormalizeInfoHash(parsed.InfoHash);
                                    }
                                }
                                catch
                                {
                                }
                            }
                            else if (release.DownloadUrl?.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                try
                                {
                                    var parsed = MagnetLinkParser.Parse(release.DownloadUrl);
                                    if (!string.IsNullOrWhiteSpace(parsed?.InfoHash))
                                    {
                                        release.InfoHash = MagnetLinkParser.NormalizeInfoHash(parsed.InfoHash);
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }

                        var releaseId = GetReleaseId(release);
                        if (this.IsAlreadyGrabbed(release, releaseId))
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
                                        var magnetInfoHash = MagnetLinkParser.NormalizeInfoHash(MagnetLinkParser.Parse(release.MagnetUrl)?.InfoHash);
                                        if (!string.IsNullOrWhiteSpace(magnetInfoHash))
                                        {
                                            var existingTorrent = this.torrentService?.GetByInfoHash(magnetInfoHash);
                                            var existingHistory = this.downloadHistoryService?.GetByInfoHash(magnetInfoHash);

                                            if (existingTorrent != null || existingHistory != null)
                                            {
                                                if (!string.IsNullOrEmpty(releaseId))
                                                {
                                                    this.grabbedReleaseIds.TryAdd(releaseId, 0);
                                                }

                                                this.grabbedReleaseIds.TryAdd(magnetInfoHash, 0);
                                                this.logger.Info("Release '{0}' with infohash '{1}' has already been grabbed. Skipping duplicate.", release.Title, magnetInfoHash);
                                                break;
                                            }
                                        }

                                        addedTorrent = await this.torrentService.AddFromMagnetAsync(release.MagnetUrl, categoryName, savePath);
                                        grabbed = true;
                                    }
                                    else if (!string.IsNullOrEmpty(release.DownloadUrl))
                                    {
                                        if (release.DownloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var magnetInfoHash = MagnetLinkParser.NormalizeInfoHash(MagnetLinkParser.Parse(release.DownloadUrl)?.InfoHash);
                                            if (!string.IsNullOrWhiteSpace(magnetInfoHash))
                                            {
                                                var existingTorrent = this.torrentService?.GetByInfoHash(magnetInfoHash);
                                                var existingHistory = this.downloadHistoryService?.GetByInfoHash(magnetInfoHash);

                                                if (existingTorrent != null || existingHistory != null)
                                                {
                                                    if (!string.IsNullOrEmpty(releaseId))
                                                    {
                                                        this.grabbedReleaseIds.TryAdd(releaseId, 0);
                                                    }

                                                    this.grabbedReleaseIds.TryAdd(magnetInfoHash, 0);
                                                    this.logger.Info("Release '{0}' with infohash '{1}' has already been grabbed. Skipping duplicate.", release.Title, magnetInfoHash);
                                                    break;
                                                }
                                            }

                                            addedTorrent = await this.torrentService.AddFromMagnetAsync(release.DownloadUrl, categoryName, savePath);
                                            grabbed = true;
                                        }
                                        else
                                        {
                                            var torrentBytes = await this.safeHttpClientService.DownloadBytesAsync(release.DownloadUrl, maxSizeBytes: 10 * 1024 * 1024);
                                            var parsed = this.torrentFileParser.Parse(torrentBytes);
                                            var parsedInfoHash = MagnetLinkParser.NormalizeInfoHash(parsed?.InfoHash);

                                            if (!string.IsNullOrWhiteSpace(parsedInfoHash))
                                            {
                                                var existingTorrent = this.torrentService?.GetByInfoHash(parsedInfoHash);
                                                var existingHistory = this.downloadHistoryService?.GetByInfoHash(parsedInfoHash);

                                                if (existingTorrent != null || existingHistory != null)
                                                {
                                                    if (!string.IsNullOrEmpty(releaseId))
                                                    {
                                                        this.grabbedReleaseIds.TryAdd(releaseId, 0);
                                                    }

                                                    this.grabbedReleaseIds.TryAdd(parsedInfoHash, 0);
                                                    this.logger.Info("Release '{0}' with infohash '{1}' has already been grabbed. Skipping duplicate.", release.Title, parsedInfoHash);
                                                    break;
                                                }
                                            }

                                            addedTorrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, categoryName, savePath, false, torrentBytes);
                                            grabbed = true;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Error(ex, "Failed to grab release {0}", release.Title);
                                }

                                if (grabbed && addedTorrent != null)
                                {
                                    var normalizedAddedHash = MagnetLinkParser.NormalizeInfoHash(addedTorrent.InfoHash);
                                    var existingHistoryEntry = !string.IsNullOrEmpty(normalizedAddedHash)
                                        ? this.downloadHistoryService?.GetByInfoHash(normalizedAddedHash)
                                        : null;

                                    if (existingHistoryEntry != null)
                                    {
                                        if (!string.IsNullOrEmpty(releaseId))
                                        {
                                            this.grabbedReleaseIds.TryAdd(releaseId, 0);
                                        }

                                        if (!string.IsNullOrEmpty(normalizedAddedHash))
                                        {
                                            this.grabbedReleaseIds.TryAdd(normalizedAddedHash, 0);
                                        }

                                        this.logger.Info("Release '{0}' with infohash '{1}' already exists in download history. Skipping duplicate recording.", release.Title, normalizedAddedHash);
                                        break;
                                    }

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

                                    if (!string.IsNullOrEmpty(normalizedAddedHash))
                                    {
                                        this.grabbedReleaseIds.TryAdd(normalizedAddedHash, 0);
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
        finally
        {
            this.syncLock.Release();
        }
    }

    private bool IsAlreadyGrabbed(TorznabSearchResult release, string releaseId)
    {
        if (!string.IsNullOrEmpty(releaseId) && this.grabbedReleaseIds.ContainsKey(releaseId))
        {
            return true;
        }

        var candidateHash = release?.InfoHash;

        if (string.IsNullOrWhiteSpace(candidateHash) && !string.IsNullOrWhiteSpace(release?.MagnetUrl))
        {
            try
            {
                candidateHash = MagnetLinkParser.Parse(release.MagnetUrl)?.InfoHash;
            }
            catch
            {
                // Ignore parse errors
            }
        }

        if (string.IsNullOrWhiteSpace(candidateHash) && release?.DownloadUrl?.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                candidateHash = MagnetLinkParser.Parse(release.DownloadUrl)?.InfoHash;
            }
            catch
            {
                // Ignore parse errors
            }
        }

        if (!string.IsNullOrWhiteSpace(candidateHash))
        {
            candidateHash = MagnetLinkParser.NormalizeInfoHash(candidateHash);
            if (this.torrentService?.GetByInfoHash(candidateHash) != null ||
                this.downloadHistoryService?.GetByInfoHash(candidateHash) != null)
            {
                if (!string.IsNullOrEmpty(releaseId))
                {
                    this.grabbedReleaseIds.TryAdd(releaseId, 0);
                }

                this.grabbedReleaseIds.TryAdd(candidateHash, 0);
                return true;
            }
        }

        return false;
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
            return MagnetLinkParser.NormalizeInfoHash(release.InfoHash);
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

        var releaseTitle = release.Title ?? string.Empty;

        // 1. MustContain Regex
        if (!string.IsNullOrWhiteSpace(rule.MustContain))
        {
            try
            {
                if (!Regex.IsMatch(releaseTitle, rule.MustContain, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, DefaultRegexTimeout))
                {
                    return false;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                this.logger.Warn(ex, "Regex timeout evaluating MustContain pattern '{0}' for rule '{1}'. Skipping release.", rule.MustContain, rule.Name);
                return false;
            }
            catch (ArgumentException ex)
            {
                this.logger.Warn(ex, "Invalid MustContain regex pattern '{0}' for rule '{1}'.", rule.MustContain, rule.Name);
                return false;
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Unexpected error evaluating MustContain regex pattern '{0}' for rule '{1}'.", rule.MustContain, rule.Name);
                return false;
            }
        }

        // 2. MustNotContain Regex
        if (!string.IsNullOrWhiteSpace(rule.MustNotContain))
        {
            try
            {
                if (Regex.IsMatch(releaseTitle, rule.MustNotContain, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, DefaultRegexTimeout))
                {
                    return false;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                this.logger.Warn(ex, "Regex timeout evaluating MustNotContain pattern '{0}' for rule '{1}'. Skipping release.", rule.MustNotContain, rule.Name);
                return false;
            }
            catch (ArgumentException ex)
            {
                this.logger.Warn(ex, "Invalid MustNotContain regex pattern '{0}' for rule '{1}'.", rule.MustNotContain, rule.Name);
                return false;
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Unexpected error evaluating MustNotContain regex pattern '{0}' for rule '{1}'.", rule.MustNotContain, rule.Name);
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

        // 6. MaxAgeDays
        if (rule.MaxAgeDays > 0 && release.PublishDate != default)
        {
            var publishDateUtc = release.PublishDate.Kind switch
            {
                DateTimeKind.Utc => release.PublishDate,
                DateTimeKind.Local => release.PublishDate.ToUniversalTime(),
                _ => DateTime.SpecifyKind(release.PublishDate, DateTimeKind.Utc),
            };

            if ((DateTime.UtcNow - publishDateUtc).TotalDays > rule.MaxAgeDays)
            {
                return false;
            }
        }

        // 7. FreeleechOnly
        if (rule.FreeleechOnly && !release.IsFreeleech)
        {
            return false;
        }

        return true;
    }
}

public class BoundedSet<T>
{
    private readonly int capacity;
    private readonly ConcurrentDictionary<T, byte> set;
    private readonly ConcurrentQueue<T> queue = new();

    public BoundedSet(int capacity, IEqualityComparer<T> comparer = null)
    {
        this.capacity = capacity;
        this.set = new ConcurrentDictionary<T, byte>(comparer ?? EqualityComparer<T>.Default);
    }

    public bool ContainsKey(T item) => item != null && this.set.ContainsKey(item);

    public bool TryAdd(T item, byte value = 0)
    {
        if (item == null)
        {
            return false;
        }

        if (this.set.TryAdd(item, value))
        {
            this.queue.Enqueue(item);
            while (this.set.Count > this.capacity && this.queue.TryDequeue(out var oldest))
            {
                this.set.TryRemove(oldest, out _);
            }

            return true;
        }

        return false;
    }

    public int Count => this.set.Count;
}
