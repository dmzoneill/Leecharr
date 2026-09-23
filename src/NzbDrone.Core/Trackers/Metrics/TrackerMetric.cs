// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers.Metrics;

public class TrackerMetric : ModelBase
{
    public string TrackerUrl { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public string Protocol { get; set; } = "http";

    public int Port { get; set; } = 80;

    public string Status { get; set; } = "Working";

    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

    public DateTime? LastAnnounce { get; set; }

    public DateTime? LastScrape { get; set; }

    public DateTime? LastSuccess { get; set; }

    public DateTime? LastErrorTime { get; set; }

    public string LastErrorMessage { get; set; }

    public long TotalAnnounces { get; set; }

    public long SuccessfulAnnounces { get; set; }

    public long FailedAnnounces { get; set; }

    public long TotalScrapes { get; set; }

    public long SuccessfulScrapes { get; set; }

    public long FailedScrapes { get; set; }

    public long TotalUploaded { get; set; }

    public long TotalDownloaded { get; set; }

    public long TotalLeft { get; set; }

    public long SessionUploaded { get; set; }

    public long SessionDownloaded { get; set; }

    public int TotalTorrentsTracked { get; set; }

    public int LastSeeders { get; set; }

    public int LastLeechers { get; set; }

    public int LastPeers { get; set; }

    public long TotalPeersDiscovered { get; set; }

    public double AvgResponseTimeMs { get; set; }

    public long LastResponseTimeMs { get; set; }

    public long MinResponseTimeMs { get; set; }

    public long MaxResponseTimeMs { get; set; }

    public int ConsecutiveFailures { get; set; }

    [Ignore]
    public double LatencyP50Ms { get; set; }

    [Ignore]
    public double LatencyP95Ms { get; set; }

    [Ignore]
    public double LatencyP99Ms { get; set; }

    [Ignore]
    public double P50LatencyMs { get => this.LatencyP50Ms; set => this.LatencyP50Ms = value; }

    [Ignore]
    public double P95LatencyMs { get => this.LatencyP95Ms; set => this.LatencyP95Ms = value; }

    [Ignore]
    public double P99LatencyMs { get => this.LatencyP99Ms; set => this.LatencyP99Ms = value; }

    [Ignore]
    public double P50ResponseTimeMs { get => this.LatencyP50Ms; set => this.LatencyP50Ms = value; }

    [Ignore]
    public double P95ResponseTimeMs { get => this.LatencyP95Ms; set => this.LatencyP95Ms = value; }

    [Ignore]
    public double P99ResponseTimeMs { get => this.LatencyP99Ms; set => this.LatencyP99Ms = value; }
}
