// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Trackers;

public class TrackerMetricUpdatedEvent : IEvent
{
    public string TrackerUrl { get; set; }

    public long TotalAnnounces { get; set; }

    public long TotalUploaded { get; set; }

    public long TotalDownloaded { get; set; }

    public string Status { get; set; }

    public TrackerMetricUpdatedEvent()
    {
    }

    public TrackerMetricUpdatedEvent(string trackerUrl, long totalAnnounces, long totalUploaded, long totalDownloaded, string status)
    {
        this.TrackerUrl = trackerUrl;
        this.TotalAnnounces = totalAnnounces;
        this.TotalUploaded = totalUploaded;
        this.TotalDownloaded = totalDownloaded;
        this.Status = status;
    }
}

public class TrackerAnnounceEvent : IEvent
{
    public string TrackerUrl { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int PeersCount { get; set; }

    public long ResponseTimeMs { get; set; }

    public bool IsSuccess { get; set; }

    public string ErrorMessage { get; set; }

    public TrackerAnnounceEvent()
    {
    }

    public TrackerAnnounceEvent(string trackerUrl, int seeders, int leechers, int peersCount, long responseTimeMs, bool isSuccess, string errorMessage = null)
    {
        this.TrackerUrl = trackerUrl;
        this.Seeders = seeders;
        this.Leechers = leechers;
        this.PeersCount = peersCount;
        this.ResponseTimeMs = responseTimeMs;
        this.IsSuccess = isSuccess;
        this.ErrorMessage = errorMessage;
    }
}

public class TrackerScrapeEvent : IEvent
{
    public string TrackerUrl { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int Completed { get; set; }

    public long ResponseTimeMs { get; set; }

    public bool IsSuccess { get; set; }

    public string ErrorMessage { get; set; }

    public TrackerScrapeEvent()
    {
    }

    public TrackerScrapeEvent(string trackerUrl, int seeders, int leechers, int completed, long responseTimeMs, bool isSuccess, string errorMessage = null)
    {
        this.TrackerUrl = trackerUrl;
        this.Seeders = seeders;
        this.Leechers = leechers;
        this.Completed = completed;
        this.ResponseTimeMs = responseTimeMs;
        this.IsSuccess = isSuccess;
        this.ErrorMessage = errorMessage;
    }
}
