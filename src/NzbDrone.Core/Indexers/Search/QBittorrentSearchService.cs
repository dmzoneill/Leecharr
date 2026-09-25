// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Indexers.Search;

public class QBittorrentSearchJob
{
    public int Id { get; set; }

    public string Pattern { get; set; }

    public string Status { get; set; } = "Running";

    public List<QBittorrentSearchResultItem> Results { get; set; } = new();

    public HashSet<string> SeenKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public CancellationTokenSource Cts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StoppedAt { get; set; }
}

public class QBittorrentSearchService : IQBittorrentSearchService, IDisposable
{
    public const int DefaultMaxJobs = 100;
    public static readonly TimeSpan DefaultJobTtl = TimeSpan.FromMinutes(30);

    private readonly IIndexerRepository indexerRepository;
    private readonly ITorznabClient torznabClient;
    private readonly IConfigService configService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private static readonly Logger StaticLogger = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<int, QBittorrentSearchJob> activeJobs = new();
    private readonly Timer cleanupTimer;
    private readonly int maxJobs;
    private readonly TimeSpan jobTtl;
    private int nextJobId = 1;
    private bool disposed;

    public QBittorrentSearchService(
        IIndexerRepository indexerRepository = null,
        ITorznabClient torznabClient = null,
        IConfigService configService = null,
        int maxJobs = DefaultMaxJobs,
        TimeSpan? jobTtl = null)
    {
        this.indexerRepository = indexerRepository;
        this.torznabClient = torznabClient ?? new TorznabClient(configService);
        this.configService = configService;
        this.maxJobs = maxJobs > 0 ? maxJobs : DefaultMaxJobs;
        this.jobTtl = jobTtl ?? DefaultJobTtl;

        var timerInterval = this.jobTtl > TimeSpan.Zero && this.jobTtl < TimeSpan.FromMinutes(5)
            ? this.jobTtl
            : TimeSpan.FromMinutes(5);

        this.cleanupTimer = new Timer(
            _ =>
            {
                try
                {
                    this.PruneExpiredJobs();
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to prune expired QBittorrent search jobs");
                }
            },
            null,
            timerInterval,
            timerInterval);
    }

    public int PruneExpiredJobs(TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? this.jobTtl;
        var now = DateTime.UtcNow;
        var expiredJobIds = this.activeJobs.Values
            .Where(j => (now - j.CreatedAt > effectiveTtl) ||
                        (j.StoppedAt.HasValue && now - j.StoppedAt.Value > effectiveTtl))
            .Select(j => j.Id)
            .ToList();

        var prunedCount = 0;
        foreach (var id in expiredJobIds)
        {
            if (this.activeJobs.TryRemove(id, out var job))
            {
                SafeDisposeJob(job, cleanResults: true);
                prunedCount++;
            }
        }

        return prunedCount;
    }

    public int StartSearch(string pattern, string plugins = null, string category = null)
    {
        this.PruneExpiredJobs();

        while (this.activeJobs.Count >= this.maxJobs)
        {
            var oldestCompleted = this.activeJobs.Values
                .Where(j => j.Status != "Running")
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefault();

            var target = oldestCompleted ?? this.activeJobs.Values.OrderBy(j => j.CreatedAt).FirstOrDefault();
            if (target == null)
            {
                break;
            }

            if (this.activeJobs.TryRemove(target.Id, out var removed))
            {
                SafeDisposeJob(removed, cleanResults: true);
            }
        }

        var id = Interlocked.Increment(ref this.nextJobId);
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var job = new QBittorrentSearchJob
        {
            Id = id,
            Pattern = pattern,
            Status = "Running",
            Cts = cts,
        };

        this.activeJobs[id] = job;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    var indexers = this.indexerRepository != null
                        ? this.indexerRepository.GetSearchEnabled().ToList()
                        : new List<IndexerDefinition>();

                    if (!string.IsNullOrWhiteSpace(plugins) &&
                        !string.Equals(plugins, "all", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(plugins, "enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var pluginSet = plugins.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        indexers = indexers.Where(i => pluginSet.Contains(i.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                    }

                    if (indexers.Count == 0)
                    {
                        this.logger.Warn("No matching search-enabled indexers configured in Leecharr.");
                        job.Status = "Stopped";
                        job.StoppedAt = DateTime.UtcNow;
                        return;
                    }

                    var trimmedCategory = category?.Trim();
                    int? categoryId = int.TryParse(trimmedCategory, out var parsedCat) && parsedCat > 0
                        ? parsedCat
                        : trimmedCategory?.ToLowerInvariant() switch
                        {
                            "movies" => 2000,
                            "movies_hd" or "movies-hd" or "movies/hd" => 2040,
                            "movies_sd" or "movies-sd" or "movies/sd" => 2030,
                            "movies_uhd" or "movies-uhd" or "movies/uhd" or "movies_4k" or "movies-4k" or "movies/4k" => 2045,
                            "movies_bluray" or "movies-bluray" or "movies/bluray" => 2050,
                            "movies_3d" or "movies-3d" or "movies/3d" => 2060,
                            "tv" => 5000,
                            "tv_hd" or "tv-hd" or "tv/hd" => 5040,
                            "tv_sd" or "tv-sd" or "tv/sd" => 5030,
                            "tv_uhd" or "tv-uhd" or "tv/uhd" or "tv_4k" or "tv-4k" or "tv/4k" => 5045,
                            "tv_anime" or "tv-anime" or "tv/anime" or "anime" => 5070,
                            "tv_doc" or "tv-doc" or "tv/doc" or "tv_documentary" or "tv/documentary" => 5080,
                            "audio" or "music" => 3000,
                            "music_mp3" or "music-mp3" or "music/mp3" or "audio_mp3" or "audio-mp3" or "audio/mp3" => 3010,
                            "music_flac" or "music-flac" or "music/flac" or "music_lossless" or "music-lossless" or "music/lossless" or "audio_lossless" or "audio-lossless" or "audio/lossless" or "audio_flac" or "audio-flac" or "audio/flac" => 3040,
                            "audiobook" or "audiobooks" or "audio_audiobook" or "audio/audiobook" => 3030,
                            "games" => 1000,
                            "games_pc" or "games-pc" or "games/pc" => 1010,
                            "games_console" or "games-console" or "games/console" => 1020,
                            "pc" or "software" => 4000,
                            "software_pc" or "software-pc" or "software/pc" => 4010,
                            "software_mac" or "software-mac" or "software/mac" => 4020,
                            "books" or "ebooks" or "ebook" => 7000,
                            "books_ebook" or "books-ebook" or "books/ebook" => 7020,
                            "books_comics" or "books-comics" or "books/comics" or "comics" => 7030,
                            "books_mags" or "books-mags" or "books/mags" or "magazines" or "mags" => 7010,
                            _ => null,
                        };

                    var searchLimit = this.configService?.TorznabMaxPageSize > 0
                        ? this.configService.TorznabMaxPageSize
                        : 100;

                    var tasks = indexers.Select(async indexer =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            var results = await this.torznabClient.SearchAsync(
                                indexer,
                                pattern,
                                categoryId: categoryId,
                                limit: searchLimit,
                                cancellationToken: token);

                            if (results == null || results.Count == 0)
                            {
                                return;
                            }

                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            lock (job.Results)
                            {
                                foreach (var item in results)
                                {
                                    var infoHash = !string.IsNullOrWhiteSpace(item.InfoHash)
                                        ? item.InfoHash.Trim()
                                        : ExtractBtih(item.MagnetUrl ?? item.DownloadUrl);
                                    var fileUrl = (item.MagnetUrl ?? item.DownloadUrl ?? string.Empty).Trim();
                                    var descrLink = (item.Guid ?? item.DownloadUrl ?? string.Empty).Trim();
                                    var guid = (item.Guid ?? string.Empty).Trim();

                                    var isDuplicate = false;
                                    if (!string.IsNullOrWhiteSpace(infoHash) && job.SeenKeys.Contains("hash:" + infoHash))
                                    {
                                        isDuplicate = true;
                                    }
                                    else if (!string.IsNullOrWhiteSpace(fileUrl) && job.SeenKeys.Contains("url:" + fileUrl))
                                    {
                                        isDuplicate = true;
                                    }
                                    else if (!string.IsNullOrWhiteSpace(descrLink) && job.SeenKeys.Contains("descr:" + descrLink))
                                    {
                                        isDuplicate = true;
                                    }
                                    else if (!string.IsNullOrWhiteSpace(guid) && job.SeenKeys.Contains("guid:" + guid))
                                    {
                                        isDuplicate = true;
                                    }

                                    if (isDuplicate)
                                    {
                                        var existing = job.Results.FirstOrDefault(r =>
                                            (!string.IsNullOrWhiteSpace(infoHash) && string.Equals(ExtractBtih(r.FileUrl), infoHash, StringComparison.OrdinalIgnoreCase)) ||
                                            (!string.IsNullOrWhiteSpace(fileUrl) && string.Equals(r.FileUrl, fileUrl, StringComparison.OrdinalIgnoreCase)) ||
                                            (!string.IsNullOrWhiteSpace(descrLink) && string.Equals(r.DescrLink, descrLink, StringComparison.OrdinalIgnoreCase)));

                                        if (existing != null && item.Seeders > existing.NbSeeders)
                                        {
                                            existing.NbSeeders = item.Seeders;
                                            existing.NbLeechers = Math.Max(existing.NbLeechers, item.Leechers);
                                        }
                                    }
                                    else
                                    {
                                        if (!string.IsNullOrWhiteSpace(infoHash))
                                        {
                                            job.SeenKeys.Add("hash:" + infoHash);
                                        }

                                        if (!string.IsNullOrWhiteSpace(fileUrl))
                                        {
                                            job.SeenKeys.Add("url:" + fileUrl);
                                        }

                                        if (!string.IsNullOrWhiteSpace(descrLink))
                                        {
                                            job.SeenKeys.Add("descr:" + descrLink);
                                        }

                                        if (!string.IsNullOrWhiteSpace(guid))
                                        {
                                            job.SeenKeys.Add("guid:" + guid);
                                        }

                                        job.Results.Add(new QBittorrentSearchResultItem
                                        {
                                            DescrLink = descrLink,
                                            FileName = item.Title ?? "Unknown",
                                            FileSize = item.Size,
                                            FileUrl = fileUrl,
                                            NbLeechers = item.Leechers,
                                            NbSeeders = item.Seeders,
                                            SiteUrl = !string.IsNullOrWhiteSpace(indexer.Url)
                                                ? indexer.Url
                                                : (!string.IsNullOrWhiteSpace(item.DetailsUrl) ? item.DetailsUrl : (item.IndexerName ?? indexer.Name ?? "Leecharr")),
                                        });
                                    }
                                }

                                job.Results.Sort((a, b) =>
                                {
                                    var cmp = b.NbSeeders.CompareTo(a.NbSeeders);
                                    return cmp != 0 ? cmp : string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                                });
                            }
                        }
                        catch (OperationCanceledException ex)
                        {
                            this.logger.Trace(ex, "Search cancelled gracefully on indexer {0}", indexer.Name);
                        }
                        catch (ObjectDisposedException ex)
                        {
                            this.logger.Trace(ex, "CTS disposed during cancellation on indexer {0}", indexer.Name);
                        }
                        catch (Exception ex)
                        {
                            this.logger.Warn(ex, "Search failed on indexer {0}", indexer.Name);
                        }
                    });

                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException ex)
                {
                    this.logger.Trace(ex, "Search job {0} cancelled gracefully", id);
                }
                catch (ObjectDisposedException ex)
                {
                    this.logger.Trace(ex, "Search job {0} CTS disposed during cancellation", id);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Unexpected error in background search job {0}", id);
                }
                finally
                {
                    job.Status = "Stopped";
                    job.StoppedAt ??= DateTime.UtcNow;
                }
            });

        return id;
    }

    public bool StopSearch(int id)
    {
        if (this.activeJobs.TryGetValue(id, out var job))
        {
            job.Status = "Stopped";
            job.StoppedAt = DateTime.UtcNow;
            SafeDisposeJob(job, cleanResults: false);
            return true;
        }

        return false;
    }

    public bool DeleteSearch(int id)
    {
        if (this.activeJobs.TryRemove(id, out var job))
        {
            SafeDisposeJob(job, cleanResults: true);
            return true;
        }

        return false;
    }

    public QBittorrentSearchStatus GetStatus(int id)
    {
        if (this.activeJobs.TryGetValue(id, out var job))
        {
            lock (job.Results)
            {
                return new QBittorrentSearchStatus
                {
                    Id = job.Id,
                    Status = job.Status,
                    Total = job.Results.Count,
                };
            }
        }

        return null;
    }

    public List<QBittorrentSearchStatus> GetAllStatuses()
    {
        var list = new List<QBittorrentSearchStatus>();
        foreach (var job in this.activeJobs.Values)
        {
            lock (job.Results)
            {
                list.Add(new QBittorrentSearchStatus
                {
                    Id = job.Id,
                    Status = job.Status,
                    Total = job.Results.Count,
                });
            }
        }

        return list;
    }

    public QBittorrentSearchResultsResponse GetResults(int id, int limit = 0, int offset = 0)
    {
        if (this.activeJobs.TryGetValue(id, out var job))
        {
            lock (job.Results)
            {
                var skipCount = offset < 0 ? Math.Max(0, job.Results.Count + offset) : offset;
                var query = job.Results.Skip(skipCount);
                if (limit > 0)
                {
                    query = query.Take(limit);
                }

                return new QBittorrentSearchResultsResponse
                {
                    Results = query.ToList(),
                    Status = job.Status,
                    Total = job.Results.Count,
                };
            }
        }

        return null;
    }

    public List<object> GetPlugins()
    {
        var plugins = new List<object>();
        var indexers = this.indexerRepository != null
            ? this.indexerRepository.GetSearchEnabled().ToList()
            : new List<IndexerDefinition>();

        if (indexers.Count > 0)
        {
            foreach (var idx in indexers)
            {
                plugins.Add(new
                {
                    name = idx.Name,
                    fullName = $"{idx.Name} (Torznab)",
                    version = "1.0",
                    url = idx.Url,
                    enabled = idx.Enable,
                    supportedCategories = new[] { "all", "movies", "tv", "music", "anime", "software" },
                });
            }
        }
        else
        {
            plugins.Add(new
            {
                name = "Leecharr Torznab Hub",
                fullName = "Leecharr Unified Torznab Indexer Hub",
                version = "1.0",
                url = "https://github.com/Leecharr/Leecharr",
                enabled = true,
                supportedCategories = new[] { "all", "movies", "tv", "music", "anime", "software" },
            });
        }

        return plugins;
    }

    public List<string> GetCategories()
    {
        return new List<string>
        {
            "all",
            "movies",
            "movies_hd",
            "movies_sd",
            "movies_uhd",
            "tv",
            "tv_hd",
            "tv_sd",
            "tv_uhd",
            "music",
            "music_mp3",
            "music_flac",
            "audiobook",
            "games",
            "anime",
            "software",
            "books",
            "ebooks",
        };
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal QBittorrentSearchJob GetJob(int id)
    {
        this.activeJobs.TryGetValue(id, out var job);
        return job;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                this.cleanupTimer?.Dispose();

                foreach (var job in this.activeJobs.Values)
                {
                    SafeDisposeJob(job, cleanResults: true);
                }

                this.activeJobs.Clear();
            }

            this.disposed = true;
        }
    }

    private static string ExtractBtih(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var idx = url.IndexOf("urn:btih:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var hashPart = url.Substring(idx + "urn:btih:".Length);
        var ampIdx = hashPart.IndexOf('&');
        if (ampIdx >= 0)
        {
            hashPart = hashPart.Substring(0, ampIdx);
        }

        var clean = hashPart.Trim();
        return clean.Length > 0 ? clean : null;
    }

    private static void SafeDisposeJob(QBittorrentSearchJob job, bool cleanResults = false)
    {
        if (job?.Cts != null)
        {
            try
            {
                job.Cts.Cancel();
            }
            catch (Exception ex)
            {
                StaticLogger.Trace(ex, "Search job CTS cancellation failed (already canceled/disposed)");
            }

            try
            {
                job.Cts.Dispose();
            }
            catch (Exception ex)
            {
                StaticLogger.Trace(ex, "Search job CTS disposal failed (already disposed)");
            }
        }

        if (cleanResults && job != null)
        {
            lock (job.Results)
            {
                job.Results.Clear();
                job.SeenKeys.Clear();
            }
        }
    }
}
