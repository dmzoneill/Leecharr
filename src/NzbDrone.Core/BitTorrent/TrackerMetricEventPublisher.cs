// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Diagnostics;
using System.Linq;
using MonoTorrent.Client;
using MonoTorrent.Trackers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.BitTorrent;

internal static class TrackerMetricEventPublisher
{
    public static void PublishAnnounce(
        IEventAggregator eventAggregator,
        TorrentManager manager,
        int torrentId,
        ITracker tracker,
        long responseTimeMs,
        bool success,
        string errorMessage = null)
    {
        if (eventAggregator == null || tracker?.Uri == null || torrentId <= 0)
        {
            return;
        }

        var tier = manager?.TrackerManager?.Tiers?.FirstOrDefault(t => t.Trackers?.Contains(tracker) == true);
        var (seeders, leechers, _) = ExtractScrapeCounts(tier, manager);
        var transfer = GetTransferSnapshot(manager);

        eventAggregator.PublishEvent(new TrackerAnnounceEvent
        {
            TrackerUrl = tracker.Uri.ToString(),
            TorrentId = torrentId,
            Uploaded = transfer.Uploaded,
            Downloaded = transfer.Downloaded,
            Left = transfer.Left,
            Seeders = seeders,
            Leechers = leechers,
            PeersCount = 0,
            ResponseTimeMs = responseTimeMs,
            IsSuccess = success,
            ErrorMessage = errorMessage ?? tracker.FailureMessage,
        });
    }

    public static void PublishAnnouncesForAllTrackers(
        IEventAggregator eventAggregator,
        TorrentManager manager,
        int torrentId,
        long responseTimeMs)
    {
        if (eventAggregator == null || manager?.TrackerManager?.Tiers == null || torrentId <= 0)
        {
            return;
        }

        foreach (var tier in manager.TrackerManager.Tiers)
        {
            if (tier?.Trackers == null)
            {
                continue;
            }

            foreach (var tracker in tier.Trackers)
            {
                if (tracker?.Uri == null)
                {
                    continue;
                }

                var success = MultiTrackerAnnouncePolicy.IsAnnounceSuccessful(tracker);
                PublishAnnounce(eventAggregator, manager, torrentId, tracker, responseTimeMs, success);
            }
        }
    }

    public static void PublishScrapesForAllTrackers(
        IEventAggregator eventAggregator,
        TorrentManager manager,
        int torrentId,
        long responseTimeMs)
    {
        if (eventAggregator == null || manager?.TrackerManager?.Tiers == null || torrentId <= 0)
        {
            return;
        }

        foreach (var tier in manager.TrackerManager.Tiers)
        {
            if (tier?.Trackers == null)
            {
                continue;
            }

            var (seeders, leechers, completed) = ExtractScrapeCounts(tier, manager);
            foreach (var tracker in tier.Trackers)
            {
                if (tracker?.Uri == null)
                {
                    continue;
                }

                var success = tier.LastScrapeSucceeded &&
                    tracker.Status == TrackerState.Ok &&
                    string.IsNullOrWhiteSpace(tracker.FailureMessage);

                eventAggregator.PublishEvent(new TrackerScrapeEvent
                {
                    TrackerUrl = tracker.Uri.ToString(),
                    TorrentId = torrentId,
                    Seeders = seeders,
                    Leechers = leechers,
                    Completed = completed,
                    ResponseTimeMs = responseTimeMs,
                    IsSuccess = success,
                    ErrorMessage = tracker.FailureMessage,
                });
            }
        }
    }

    public static long MeasureElapsedMs(Stopwatch stopwatch)
    {
        if (stopwatch == null)
        {
            return 0;
        }

        stopwatch.Stop();
        return Math.Max(0, stopwatch.ElapsedMilliseconds);
    }

    private static (long Uploaded, long Downloaded, long Left) GetTransferSnapshot(TorrentManager manager)
    {
        if (manager == null)
        {
            return (0, 0, 0);
        }

        var uploaded = manager.Monitor?.DataBytesSent ?? 0;
        var downloaded = manager.Monitor?.DataBytesReceived ?? 0;
        var total = manager.Torrent?.Size ?? 0;
        long left = 0;
        if (manager.Complete)
        {
            left = 0;
        }
        else if (total > 0)
        {
            var progress = Math.Clamp(manager.Progress, 0, 100);
            left = Math.Max(0, total - (long)(total * (progress / 100.0)));
        }

        return (uploaded, downloaded, left);
    }

    private static (int Seeders, int Leechers, int Completed) ExtractScrapeCounts(TrackerTier tier, TorrentManager manager)
    {
        if (tier?.ScrapeInfo == null || tier.ScrapeInfo.Count == 0)
        {
            return (0, 0, 0);
        }

        var infoHash = manager?.Torrent?.InfoHashes?.V1OrV2;
        if (infoHash == null || !tier.ScrapeInfo.TryGetValue(infoHash, out var scrapeInfo) || scrapeInfo == null)
        {
            scrapeInfo = tier.ScrapeInfo.Values.FirstOrDefault();
        }

        if (scrapeInfo == null)
        {
            return (0, 0, 0);
        }

        return (scrapeInfo.Complete, scrapeInfo.Incomplete, scrapeInfo.Downloaded);
    }
}
