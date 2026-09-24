// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Trackers.Metrics;

public class TrackerMetricService : ITrackerMetricService, IDisposable, IAsyncDisposable
{
    private readonly ITrackerMetricRepository metricRepository;
    private readonly ITrackerMetricSnapshotRepository snapshotRepository;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly ITorrentRepository torrentRepository;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly object syncLock = new();
    private readonly ConcurrentDictionary<string, (long Uploaded, long Downloaded)> lastSeenBytes = new();

    private readonly Channel<TrackerMetricSnapshot> snapshotChannel = Channel.CreateBounded<TrackerMetricSnapshot>(
        new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });

    private readonly CancellationTokenSource cts = new();
    private readonly Task flushTask;
    private readonly Timer pruneTimer;
    private readonly SemaphoreSlim flushLock = new(1, 1);
    private readonly object flushGate = new();
    private CancellationTokenSource flushSignalCts = new();
    private volatile bool isProcessingBatch;
    private bool disposed;

    public TrackerMetricService(
        ITrackerMetricRepository metricRepository,
        ITrackerMetricSnapshotRepository snapshotRepository,
        ITrackerEntryRepository trackerEntryRepository,
        ITorrentRepository torrentRepository,
        IEventAggregator eventAggregator = null)
    {
        this.metricRepository = metricRepository;
        this.snapshotRepository = snapshotRepository;
        this.trackerEntryRepository = trackerEntryRepository;
        this.torrentRepository = torrentRepository;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        this.flushTask = Task.Run(this.ProcessSnapshotQueueAsync);
        this.pruneTimer = new Timer(
            _ =>
            {
                try
                {
                    this.PruneSnapshots(DateTime.UtcNow.AddDays(-7));
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error during automatic tracker metric snapshot prune");
                }
            },
            null,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(24));

        Task.Run(this.SeedFromExistingTrackers);
    }

    public void SeedFromExistingTrackers()
    {
        try
        {
            var trackerEntries = this.trackerEntryRepository?.All().ToList() ?? new List<TrackerEntry>();
            var torrents = this.torrentRepository?.All().ToList() ?? new List<Torrent>();

            foreach (var t in torrents)
            {
                if (!string.IsNullOrWhiteSpace(t.TrackerUrl))
                {
                    this.GetOrCreateMetric(t.TrackerUrl);
                    var key = $"{t.TrackerUrl}_{t.Id}";
                    this.lastSeenBytes.TryAdd(key, (t.Uploaded, t.Downloaded));
                }
            }

            foreach (var entry in trackerEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Url))
                {
                    var metric = this.GetOrCreateMetric(entry.Url);
                    if (metric != null && metric.TotalAnnounces == 0 && entry.TotalAnnounces > 0)
                    {
                        metric.TotalAnnounces = entry.TotalAnnounces;
                        metric.SuccessfulAnnounces = entry.SuccessfulAnnounces;
                        metric.FailedAnnounces = Math.Max(0, entry.TotalAnnounces - entry.SuccessfulAnnounces);
                        metric.LastAnnounce = entry.LastAnnounce;
                        metric.LastSeeders = entry.Seeders;
                        metric.LastLeechers = entry.Leechers;
                        if (entry.LastResponseTime > 0)
                        {
                            metric.LastResponseTimeMs = entry.LastResponseTime;
                            metric.AvgResponseTimeMs = entry.LastResponseTime;
                        }

                        this.metricRepository.Update(metric);
                    }

                    var key = $"{entry.Url}_{entry.TorrentId}";
                    this.lastSeenBytes.AddOrUpdate(key, (0, entry.Downloaded), (k, old) => (old.Uploaded, Math.Max(old.Downloaded, (long)entry.Downloaded)));
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed initializing tracker metrics from existing records");
        }
    }

    public TrackerMetric RecordAnnounce(
        string trackerUrl,
        int torrentId,
        long uploaded,
        long downloaded,
        long left,
        long responseTimeMs,
        bool success,
        int seeders,
        int leechers,
        int peersCount,
        string error = null)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        TrackerMetric metric;
        long deltaUploaded = 0;
        long deltaDownloaded = 0;

        lock (this.syncLock)
        {
            metric = this.GetOrCreateMetric(trackerUrl);
            metric.TotalAnnounces++;
            metric.LastAnnounce = now;

            if (success)
            {
                var key = $"{trackerUrl}_{torrentId}";
                if (this.lastSeenBytes.TryGetValue(key, out var lastSeen))
                {
                    deltaUploaded = Math.Max(0, uploaded - lastSeen.Uploaded);
                    deltaDownloaded = Math.Max(0, downloaded - lastSeen.Downloaded);
                }
                else
                {
                    deltaUploaded = Math.Max(0, uploaded);
                    deltaDownloaded = Math.Max(0, downloaded);
                }

                this.lastSeenBytes[key] = (uploaded, downloaded);

                UpdateLatencyMetrics(metric, responseTimeMs);
                metric.SuccessfulAnnounces++;
                metric.ConsecutiveFailures = 0;
                metric.LastSuccess = now;
                metric.LastErrorMessage = null;
                metric.Status = "Working";

                if (seeders > 0)
                {
                    metric.LastSeeders = seeders;
                }

                if (leechers > 0)
                {
                    metric.LastLeechers = leechers;
                }

                if (peersCount > 0)
                {
                    metric.LastPeers = peersCount;
                    metric.TotalPeersDiscovered += peersCount;
                }

                metric.TotalUploaded += deltaUploaded;
                metric.TotalDownloaded += deltaDownloaded;
                metric.TotalLeft = left;
                metric.SessionUploaded += deltaUploaded;
                metric.SessionDownloaded += deltaDownloaded;
            }
            else
            {
                metric.FailedAnnounces++;
                metric.ConsecutiveFailures++;
                metric.LastErrorTime = now;
                metric.LastErrorMessage = error ?? "Announce failed";

                if (metric.ConsecutiveFailures >= 5)
                {
                    metric.Status = "Offline";
                }
                else if (metric.ConsecutiveFailures >= 2)
                {
                    metric.Status = "Degraded";
                }

                this.eventAggregator?.PublishEvent(new TrackerUnreachableEvent(null, trackerUrl, error ?? "Announce failed"));
            }

            this.metricRepository.Update(metric);
            this.eventAggregator?.PublishEvent(new TrackerMetricUpdatedEvent(metric.TrackerUrl, metric.TotalAnnounces, metric.TotalUploaded, metric.TotalDownloaded, metric.Status));
        }

        this.snapshotChannel.Writer.TryWrite(new TrackerMetricSnapshot
        {
            TrackerMetricId = metric.Id,
            TrackerUrl = metric.TrackerUrl,
            Timestamp = now,
            ResponseTimeMs = responseTimeMs,
            Uploaded = deltaUploaded,
            Downloaded = deltaDownloaded,
            Seeders = seeders,
            Leechers = leechers,
            PeersDiscovered = peersCount,
            IsSuccess = success,
            Operation = "Announce",
        });

        return metric;
    }

    public TrackerMetric RecordScrape(
        string trackerUrl,
        long responseTimeMs,
        bool success,
        int seeders,
        int leechers,
        int completed,
        string error = null)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        TrackerMetric metric;

        lock (this.syncLock)
        {
            metric = this.GetOrCreateMetric(trackerUrl);
            metric.TotalScrapes++;
            metric.LastScrape = now;

            if (success)
            {
                UpdateLatencyMetrics(metric, responseTimeMs);
                metric.SuccessfulScrapes++;
                metric.ConsecutiveFailures = 0;
                metric.LastSuccess = now;
                metric.LastErrorMessage = null;
                metric.Status = "Working";
                if (seeders > 0)
                {
                    metric.LastSeeders = seeders;
                }

                if (leechers > 0)
                {
                    metric.LastLeechers = leechers;
                }
            }
            else
            {
                metric.FailedScrapes++;
                metric.LastErrorTime = now;
                metric.LastErrorMessage = error ?? "Scrape failed";
            }

            this.metricRepository.Update(metric);
            this.eventAggregator?.PublishEvent(new TrackerMetricUpdatedEvent(metric.TrackerUrl, metric.TotalAnnounces, metric.TotalUploaded, metric.TotalDownloaded, metric.Status));
        }

        this.snapshotChannel.Writer.TryWrite(new TrackerMetricSnapshot
        {
            TrackerMetricId = metric.Id,
            TrackerUrl = metric.TrackerUrl,
            Timestamp = now,
            ResponseTimeMs = responseTimeMs,
            Uploaded = 0,
            Downloaded = 0,
            Seeders = seeders,
            Leechers = leechers,
            PeersDiscovered = 0,
            IsSuccess = success,
            Operation = "Scrape",
        });

        return metric;
    }

    public List<TrackerMetric> GetAllMetrics()
    {
        var metrics = this.metricRepository.All().OrderByDescending(m => m.TotalUploaded).ThenByDescending(m => m.TotalAnnounces).ToList();
        if (metrics.Count > 0)
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-24);
                var snapshots = this.snapshotRepository.GetRecentSnapshots(since);
                var snapshotsByTracker = snapshots.GroupBy(s => s.TrackerMetricId).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var metric in metrics)
                {
                    snapshotsByTracker.TryGetValue(metric.Id, out var trackerSnapshots);
                    PopulatePercentiles(metric, trackerSnapshots);
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed populating tracker metric percentiles");
            }
        }

        return metrics;
    }

    public TrackerMetric GetMetric(int id)
    {
        var metric = this.metricRepository.Get(id);
        if (metric != null)
        {
            try
            {
                var snapshots = this.snapshotRepository.GetHistory(id, DateTime.UtcNow.AddHours(-24));
                PopulatePercentiles(metric, snapshots);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed populating tracker metric percentiles for metric {Id}", id);
            }
        }

        return metric;
    }

    public TrackerMetric GetMetricByUrl(string url)
    {
        var metric = this.metricRepository.FindByUrl(url);
        if (metric != null)
        {
            try
            {
                var snapshots = this.snapshotRepository.GetHistory(metric.Id, DateTime.UtcNow.AddHours(-24));
                PopulatePercentiles(metric, snapshots);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed populating tracker metric percentiles for url {Url}", url);
            }
        }

        return metric;
    }

    public List<TrackerMetricSnapshot> GetHistory(int id, int hours = 24, int limit = 500)
    {
        var clampedHours = Math.Clamp(hours, 1, 168);
        var clampedLimit = Math.Clamp(limit, 1, 2000);
        var since = DateTime.UtcNow.AddHours(-clampedHours);
        return this.snapshotRepository.GetHistory(id, since, clampedLimit);
    }

    public void ResetMetrics(int id)
    {
        var metric = this.metricRepository.Get(id);
        if (metric != null && !string.IsNullOrEmpty(metric.TrackerUrl))
        {
            var prefix = $"{metric.TrackerUrl}_";
            foreach (var key in this.lastSeenBytes.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                this.lastSeenBytes.TryRemove(key, out _);
            }
        }

        this.metricRepository.ResetStats(id);
    }

    public void DeleteMetric(int id)
    {
        var metric = this.metricRepository.Get(id);
        if (metric != null && !string.IsNullOrEmpty(metric.TrackerUrl))
        {
            var prefix = $"{metric.TrackerUrl}_";
            foreach (var key in this.lastSeenBytes.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                this.lastSeenBytes.TryRemove(key, out _);
            }
        }

        this.snapshotRepository.DeleteByMetricId(id);
        this.metricRepository.Delete(id);
    }

    public TrackerMetricsSummary GetSummary()
    {
        var all = this.metricRepository.All().ToList();
        var summary = new TrackerMetricsSummary
        {
            TotalTrackers = all.Count,
            HealthyTrackers = all.Count(m => m.Status == "Working"),
            DegradedTrackers = all.Count(m => m.Status == "Degraded"),
            OfflineTrackers = all.Count(m => m.Status == "Offline" || m.Status == "Failed"),
            TotalUploaded = all.Sum(m => m.TotalUploaded),
            TotalDownloaded = all.Sum(m => m.TotalDownloaded),
            TotalAnnounces = all.Sum(m => m.TotalAnnounces),
            SuccessfulAnnounces = all.Sum(m => m.SuccessfulAnnounces),
            FailedAnnounces = all.Sum(m => m.FailedAnnounces),
            TotalScrapes = all.Sum(m => m.TotalScrapes),
            SuccessfulScrapes = all.Sum(m => m.SuccessfulScrapes),
            TotalPeersDiscovered = all.Sum(m => m.TotalPeersDiscovered),
            AvgResponseTimeMs = all.Where(m => m.AvgResponseTimeMs > 0).Select(m => m.AvgResponseTimeMs).DefaultIfEmpty(0).Average(),
        };

        foreach (var m in all)
        {
            var proto = (m.Protocol ?? "http").ToUpperInvariant();
            summary.ProtocolDistribution[proto] = summary.ProtocolDistribution.GetValueOrDefault(proto, 0) + 1;

            var status = m.Status ?? "Working";
            summary.HealthDistribution[status] = summary.HealthDistribution.GetValueOrDefault(status, 0) + 1;
        }

        summary.TopUploadTrackers = all
            .OrderByDescending(m => m.TotalUploaded)
            .Take(6)
            .Select(m => new TrackerMetricItemSummary
            {
                Id = m.Id,
                TrackerUrl = m.TrackerUrl,
                Domain = m.Domain,
                Protocol = m.Protocol,
                Status = m.Status,
                TotalUploaded = m.TotalUploaded,
                TotalDownloaded = m.TotalDownloaded,
                TotalPeersDiscovered = m.TotalPeersDiscovered,
                AvgResponseTimeMs = m.AvgResponseTimeMs,
                SuccessRate = m.TotalAnnounces > 0 ? Math.Round((double)m.SuccessfulAnnounces / m.TotalAnnounces * 100.0, 1) : 100.0,
            })
            .ToList();

        summary.TopPeerTrackers = all
            .OrderByDescending(m => m.TotalPeersDiscovered)
            .Take(6)
            .Select(m => new TrackerMetricItemSummary
            {
                Id = m.Id,
                TrackerUrl = m.TrackerUrl,
                Domain = m.Domain,
                Protocol = m.Protocol,
                Status = m.Status,
                TotalUploaded = m.TotalUploaded,
                TotalDownloaded = m.TotalDownloaded,
                TotalPeersDiscovered = m.TotalPeersDiscovered,
                AvgResponseTimeMs = m.AvgResponseTimeMs,
                SuccessRate = m.TotalAnnounces > 0 ? Math.Round((double)m.SuccessfulAnnounces / m.TotalAnnounces * 100.0, 1) : 100.0,
            })
            .ToList();

        try
        {
            var now = DateTime.UtcNow;
            var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            var oldestBucketStart = currentHour.AddHours(-24);
            var aggregated = this.snapshotRepository.GetHourlyAggregatedMetrics(oldestBucketStart);

            if (aggregated != null)
            {
                var metricsByBucket = new Dictionary<string, HourlyTrackerMetricPoint>(StringComparer.OrdinalIgnoreCase);
                foreach (var point in aggregated)
                {
                    if (point.Bucket != null)
                    {
                        metricsByBucket[point.Bucket] = point;
                        if (DateTime.TryParse(point.Bucket, out var parsedDt))
                        {
                            metricsByBucket[parsedDt.ToString("yyyy-MM-dd HH:00:00")] = point;
                        }
                    }
                }

                var hourlyBuckets = new List<HourlyTrafficPoint>(24);
                for (var i = 0; i < 24; i++)
                {
                    var bucketStart = oldestBucketStart.AddHours(i);
                    var bucketKey = bucketStart.ToString("yyyy-MM-dd HH:00:00");

                    if (metricsByBucket.TryGetValue(bucketKey, out var point))
                    {
                        hourlyBuckets.Add(new HourlyTrafficPoint
                        {
                            TimeLabel = bucketStart.ToString("HH:mm"),
                            Timestamp = bucketStart,
                            Uploaded = point.Uploaded,
                            Downloaded = point.Downloaded,
                            Announces = point.Announces,
                            PeersDiscovered = point.PeersDiscovered,
                            AvgLatencyMs = point.AvgLatencyMs,
                        });
                    }
                    else
                    {
                        hourlyBuckets.Add(new HourlyTrafficPoint
                        {
                            TimeLabel = bucketStart.ToString("HH:mm"),
                            Timestamp = bucketStart,
                            Uploaded = 0,
                            Downloaded = 0,
                            Announces = 0,
                            PeersDiscovered = 0,
                            AvgLatencyMs = 0,
                        });
                    }
                }

                summary.HourlyHistory = hourlyBuckets;
            }
            else
            {
                var snapshots = this.snapshotRepository.GetRecentSnapshots(oldestBucketStart);
                if (snapshots != null)
                {
                    var hourlyBuckets = new List<HourlyTrafficPoint>(24);
                    for (var i = 0; i < 24; i++)
                    {
                        var bucketStart = oldestBucketStart.AddHours(i);
                        var bucketEnd = bucketStart.AddHours(1);

                        var inBucket = snapshots.Where(s => s.Timestamp >= bucketStart && s.Timestamp < bucketEnd).ToList();
                        hourlyBuckets.Add(new HourlyTrafficPoint
                        {
                            TimeLabel = bucketStart.ToString("HH:mm"),
                            Timestamp = bucketStart,
                            Uploaded = inBucket.Sum(s => s.Uploaded),
                            Downloaded = inBucket.Sum(s => s.Downloaded),
                            Announces = inBucket.Count(s => s.Operation == "Announce"),
                            PeersDiscovered = inBucket.Sum(s => s.PeersDiscovered),
                            AvgLatencyMs = inBucket.Where(s => s.ResponseTimeMs > 0).Select(s => (double)s.ResponseTimeMs).DefaultIfEmpty(0).Average(),
                        });
                    }

                    summary.HourlyHistory = hourlyBuckets;
                }
            }

            var recentSnapshots = this.snapshotRepository.GetRecentSnapshots(oldestBucketStart);
            var validLatencies = recentSnapshots?
                .Where(s => s.IsSuccess && s.ResponseTimeMs > 0)
                .Select(s => (double)s.ResponseTimeMs)
                .ToList() ?? new List<double>();

            if (validLatencies.Count > 0)
            {
                summary.LatencyP50Ms = CalculatePercentile(validLatencies, 50);
                summary.LatencyP95Ms = CalculatePercentile(validLatencies, 95);
                summary.LatencyP99Ms = CalculatePercentile(validLatencies, 99);
            }
            else
            {
                var trackerAverages = all.Where(m => m.AvgResponseTimeMs > 0).Select(m => m.AvgResponseTimeMs).ToList();
                summary.LatencyP50Ms = CalculatePercentile(trackerAverages, 50);
                summary.LatencyP95Ms = CalculatePercentile(trackerAverages, 95);
                summary.LatencyP99Ms = CalculatePercentile(trackerAverages, 99);
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed generating hourly traffic history or SLA percentiles");
        }

        return summary;
    }

    private TrackerMetric GetOrCreateMetric(string url)
    {
        var trimmed = (url ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var existing = this.metricRepository.FindByUrl(trimmed);
        if (existing != null)
        {
            return existing;
        }

        var (host, domain, proto, port) = ParseTrackerUrl(trimmed);
        var metric = new TrackerMetric
        {
            TrackerUrl = trimmed,
            Host = host,
            Domain = domain,
            Protocol = proto,
            Port = port,
            Status = "Working",
            FirstSeen = DateTime.UtcNow,
        };

        return this.metricRepository.Insert(metric);
    }

    private static (string Host, string Domain, string Protocol, int Port) ParseTrackerUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var proto = uri.Scheme.ToLowerInvariant();
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : (proto == "https" ? 443 : (proto == "udp" ? 1337 : 80));
                var domain = ExtractDomain(host);
                return (host, domain, proto, port);
            }
        }
        catch (UriFormatException)
        {
            // Fall back to raw url default if URI format is invalid
        }

        return (url, url, "http", 80);
    }

    private static string ExtractDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "Unknown";
        }

        var parts = host.Split('.');
        if (parts.Length >= 2)
        {
            return string.Join('.', parts.TakeLast(2));
        }

        return host;
    }

    public static void UpdateLatencyMetrics(TrackerMetric metric, long responseTimeMs)
    {
        if (metric == null)
        {
            return;
        }

        metric.LastResponseTimeMs = responseTimeMs;

        if (responseTimeMs > 0)
        {
            if (metric.MinResponseTimeMs == 0 || responseTimeMs < metric.MinResponseTimeMs)
            {
                metric.MinResponseTimeMs = responseTimeMs;
            }

            if (responseTimeMs > metric.MaxResponseTimeMs)
            {
                metric.MaxResponseTimeMs = responseTimeMs;
            }

            if (metric.AvgResponseTimeMs <= 0)
            {
                metric.AvgResponseTimeMs = responseTimeMs;
            }
            else
            {
                metric.AvgResponseTimeMs = Math.Round((metric.AvgResponseTimeMs * 0.85) + (responseTimeMs * 0.15), 1);
            }
        }
    }

    public static double CalculatePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values == null || values.Count == 0)
        {
            return 0.0;
        }

        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 1)
        {
            return Math.Round(list[0], 2);
        }

        if (percentile <= 0)
        {
            return Math.Round(list[0], 2);
        }

        if (percentile >= 100)
        {
            return Math.Round(list[^1], 2);
        }

        var rank = (percentile / 100.0) * (list.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
        {
            return Math.Round(list[lowerIndex], 2);
        }

        var fraction = rank - lowerIndex;
        var value = list[lowerIndex] + (fraction * (list[upperIndex] - list[lowerIndex]));
        return Math.Round(value, 2);
    }

    public static void PopulatePercentiles(TrackerMetric metric, IEnumerable<TrackerMetricSnapshot> snapshots)
    {
        if (metric == null)
        {
            return;
        }

        var validLatencies = snapshots?
            .Where(s => s.IsSuccess && s.ResponseTimeMs > 0)
            .Select(s => (double)s.ResponseTimeMs)
            .ToList();

        if (validLatencies != null && validLatencies.Count > 0)
        {
            metric.LatencyP50Ms = CalculatePercentile(validLatencies, 50);
            metric.LatencyP95Ms = CalculatePercentile(validLatencies, 95);
            metric.LatencyP99Ms = CalculatePercentile(validLatencies, 99);
        }
        else if (metric.AvgResponseTimeMs > 0)
        {
            metric.LatencyP50Ms = metric.AvgResponseTimeMs;
            metric.LatencyP95Ms = metric.MaxResponseTimeMs > 0 ? metric.MaxResponseTimeMs : metric.AvgResponseTimeMs;
            metric.LatencyP99Ms = metric.MaxResponseTimeMs > 0 ? metric.MaxResponseTimeMs : metric.AvgResponseTimeMs;
        }
        else
        {
            metric.LatencyP50Ms = 0;
            metric.LatencyP95Ms = 0;
            metric.LatencyP99Ms = 0;
        }
    }

    private void TriggerFlush()
    {
        lock (this.flushGate)
        {
            if (!this.flushSignalCts.IsCancellationRequested)
            {
                this.flushSignalCts.Cancel();
            }
        }
    }

    public void Flush()
    {
        this.TriggerFlush();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((this.snapshotChannel.Reader.Count > 0 || this.isProcessingBatch) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        this.flushLock.Wait(TimeSpan.FromSeconds(5));
        this.flushLock.Release();
    }

    public async Task FlushAsync()
    {
        this.TriggerFlush();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((this.snapshotChannel.Reader.Count > 0 || this.isProcessingBatch) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5).ConfigureAwait(false);
        }

        await this.flushLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        this.flushLock.Release();
    }

    public void PruneSnapshots(DateTime cutoff)
    {
        try
        {
            this.snapshotRepository.PruneOlderThan(cutoff);
            this.logger.Trace("Pruned tracker metric snapshots older than {0}", cutoff);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to prune tracker metric snapshots older than {0}", cutoff);
        }
    }

    private async Task ProcessSnapshotQueueAsync()
    {
        var batch = new List<TrackerMetricSnapshot>(50);

        try
        {
            while (await this.snapshotChannel.Reader.WaitToReadAsync(this.cts.Token).ConfigureAwait(false))
            {
                while (batch.Count < 50 && this.snapshotChannel.Reader.TryRead(out var snapshot))
                {
                    batch.Add(snapshot);
                }

                if (batch.Count > 0)
                {
                    this.isProcessingBatch = true;
                }

                if (batch.Count < 50 && batch.Count > 0)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(2);
                    while (batch.Count < 50)
                    {
                        var remaining = deadline - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero)
                        {
                            break;
                        }

                        CancellationToken flushToken;
                        lock (this.flushGate)
                        {
                            if (this.flushSignalCts.IsCancellationRequested)
                            {
                                this.flushSignalCts.Dispose();
                                this.flushSignalCts = new CancellationTokenSource();
                            }

                            flushToken = this.flushSignalCts.Token;
                        }

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(this.cts.Token, flushToken);
                        timeoutCts.CancelAfter(remaining);

                        try
                        {
                            var available = await this.snapshotChannel.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false);
                            if (!available)
                            {
                                break;
                            }

                            while (batch.Count < 50 && this.snapshotChannel.Reader.TryRead(out var snapshot))
                            {
                                batch.Add(snapshot);
                            }
                        }
                        catch (OperationCanceledException) when (!this.cts.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    this.FlushBatch(batch);
                    batch.Clear();
                    this.isProcessingBatch = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during clean processor shutdown
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Unexpected error in tracker metric snapshot processor");
        }
        finally
        {
            this.isProcessingBatch = false;
            this.DrainRemainingSnapshots();
        }
    }

    private void FlushBatch(List<TrackerMetricSnapshot> batch)
    {
        if (batch == null || batch.Count == 0)
        {
            return;
        }

        this.flushLock.Wait();
        try
        {
            this.snapshotRepository.InsertMany(batch.ToList());
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to persist batch of {0} tracker metric snapshots", batch.Count);
        }
        finally
        {
            this.flushLock.Release();
        }
    }

    private void DrainRemainingSnapshots()
    {
        var batch = new List<TrackerMetricSnapshot>(50);
        while (this.snapshotChannel.Reader.TryRead(out var snapshot))
        {
            batch.Add(snapshot);
            if (batch.Count >= 50)
            {
                this.FlushBatch(batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            this.FlushBatch(batch);
        }
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.pruneTimer?.Dispose();
        this.snapshotChannel.Writer.TryComplete();

        if (this.flushTask != null)
        {
            try
            {
                await this.flushTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error while waiting for tracker metric snapshot processor shutdown");
            }
        }

        this.cts.Dispose();
        this.flushSignalCts.Dispose();
        this.flushLock.Dispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.pruneTimer?.Dispose();

            if (disposing)
            {
                this.snapshotChannel.Writer.TryComplete();
                if (this.flushTask != null)
                {
                    try
                    {
                        if (!this.flushTask.Wait(TimeSpan.FromSeconds(5)))
                        {
                            this.cts.Cancel();
                            this.flushTask.Wait(TimeSpan.FromSeconds(1));
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error while waiting for tracker metric snapshot processor shutdown");
                    }
                }

                this.cts.Dispose();
                this.flushSignalCts.Dispose();
                this.flushLock.Dispose();
            }
        }
    }
}
