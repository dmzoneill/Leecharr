// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Trackers.Metrics;

namespace NzbDrone.Core.Trackers;

public class TrackerAnnounceScrapeEventHandler : IHandle<TrackerAnnounceEvent>, IHandle<TrackerScrapeEvent>
{
    private readonly ITrackerMetricService trackerMetricService;
    private readonly ITrackerEntryRepository trackerEntryRepository;

    public TrackerAnnounceScrapeEventHandler(
        ITrackerMetricService trackerMetricService,
        ITrackerEntryRepository trackerEntryRepository)
    {
        this.trackerMetricService = trackerMetricService;
        this.trackerEntryRepository = trackerEntryRepository;
    }

    public void Handle(TrackerAnnounceEvent message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.TrackerUrl))
        {
            return;
        }

        this.trackerMetricService.RecordAnnounce(
            message.TrackerUrl,
            message.TorrentId,
            message.Uploaded,
            message.Downloaded,
            message.Left,
            message.ResponseTimeMs,
            message.IsSuccess,
            message.Seeders,
            message.Leechers,
            message.PeersCount,
            message.ErrorMessage);

        this.UpdateTrackerEntryFromAnnounce(message);
    }

    public void Handle(TrackerScrapeEvent message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.TrackerUrl))
        {
            return;
        }

        this.trackerMetricService.RecordScrape(
            message.TrackerUrl,
            message.ResponseTimeMs,
            message.IsSuccess,
            message.Seeders,
            message.Leechers,
            message.Completed,
            message.ErrorMessage);

        this.UpdateTrackerEntryFromScrape(message);
    }

    private void UpdateTrackerEntryFromAnnounce(TrackerAnnounceEvent message)
    {
        if (message.TorrentId <= 0)
        {
            return;
        }

        var entry = this.FindTrackerEntry(message.TorrentId, message.TrackerUrl);
        if (entry == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        entry.TotalAnnounces++;
        entry.LastAnnounce = now;
        entry.LastResponseTime = message.ResponseTimeMs;

        if (message.IsSuccess)
        {
            entry.SuccessfulAnnounces++;
            entry.ConsecutiveFailures = 0;
            entry.Status = 1;
            entry.ErrorMessage = null;
            if (message.Seeders > 0)
            {
                entry.Seeders = message.Seeders;
            }

            if (message.Leechers > 0)
            {
                entry.Leechers = message.Leechers;
            }
        }
        else
        {
            entry.ConsecutiveFailures++;
            entry.Status = 2;
            entry.ErrorMessage = message.ErrorMessage ?? "Announce failed";
        }

        var interval = entry.AnnounceInterval > 0 ? entry.AnnounceInterval : 1800;
        entry.NextAnnounce = now.AddSeconds(interval);
        this.trackerEntryRepository.Update(entry);
    }

    private void UpdateTrackerEntryFromScrape(TrackerScrapeEvent message)
    {
        if (message.TorrentId <= 0)
        {
            return;
        }

        var entry = this.FindTrackerEntry(message.TorrentId, message.TrackerUrl);
        if (entry == null)
        {
            return;
        }

        if (message.IsSuccess)
        {
            if (message.Seeders > 0)
            {
                entry.Seeders = message.Seeders;
            }

            if (message.Leechers > 0)
            {
                entry.Leechers = message.Leechers;
            }

            if (message.Completed > 0)
            {
                entry.Downloaded = message.Completed;
            }

            if (entry.Status == 0)
            {
                entry.Status = 1;
            }
        }
        else if (!string.IsNullOrWhiteSpace(message.ErrorMessage))
        {
            entry.ErrorMessage = message.ErrorMessage;
        }

        this.trackerEntryRepository.Update(entry);
    }

    private TrackerEntry FindTrackerEntry(int torrentId, string trackerUrl)
    {
        var trimmed = trackerUrl?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return this.trackerEntryRepository
            .GetByTorrentId(torrentId)
            .FirstOrDefault(e => string.Equals(e.Url?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
