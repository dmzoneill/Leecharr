// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Trackers.Metrics;

public interface ITrackerMetricService : IDisposable
{
    TrackerMetric RecordAnnounce(
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
        string error = null);

    TrackerMetric RecordScrape(
        string trackerUrl,
        long responseTimeMs,
        bool success,
        int seeders,
        int leechers,
        int completed,
        string error = null);

    List<TrackerMetric> GetAllMetrics();

    TrackerMetric GetMetric(int id);

    TrackerMetric GetMetricByUrl(string url);

    TrackerMetricsSummary GetSummary();

    List<TrackerMetricSnapshot> GetHistory(int id, int hours = 24, int limit = 500);

    void ResetMetrics(int id);

    void DeleteMetric(int id);

    void SeedFromExistingTrackers();

    void Flush();

    Task FlushAsync();

    void PruneSnapshots(DateTime cutoff);
}

public class TrackerMetricsSummary
{
    public int TotalTrackers { get; set; }

    public int HealthyTrackers { get; set; }

    public int DegradedTrackers { get; set; }

    public int OfflineTrackers { get; set; }

    public long TotalUploaded { get; set; }

    public long TotalDownloaded { get; set; }

    public double GlobalRatio => this.TotalDownloaded > 0 ? Math.Round((double)this.TotalUploaded / this.TotalDownloaded, 3) : (this.TotalUploaded > 0 ? 999.0 : 0.0);

    public long TotalAnnounces { get; set; }

    public long SuccessfulAnnounces { get; set; }

    public long FailedAnnounces { get; set; }

    public double AnnounceSuccessRate => this.TotalAnnounces > 0 ? Math.Round((double)this.SuccessfulAnnounces / this.TotalAnnounces * 100.0, 1) : 100.0;

    public long TotalScrapes { get; set; }

    public long SuccessfulScrapes { get; set; }

    public long TotalPeersDiscovered { get; set; }

    public double AvgResponseTimeMs { get; set; }

    public double LatencyP50Ms { get; set; }

    public double LatencyP95Ms { get; set; }

    public double LatencyP99Ms { get; set; }

    public double P50LatencyMs { get => this.LatencyP50Ms; set => this.LatencyP50Ms = value; }

    public double P95LatencyMs { get => this.LatencyP95Ms; set => this.LatencyP95Ms = value; }

    public double P99LatencyMs { get => this.LatencyP99Ms; set => this.LatencyP99Ms = value; }

    public double P50ResponseTimeMs { get => this.LatencyP50Ms; set => this.LatencyP50Ms = value; }

    public double P95ResponseTimeMs { get => this.LatencyP95Ms; set => this.LatencyP95Ms = value; }

    public double P99ResponseTimeMs { get => this.LatencyP99Ms; set => this.LatencyP99Ms = value; }

    public Dictionary<string, int> ProtocolDistribution { get; set; } = new();

    public Dictionary<string, int> HealthDistribution { get; set; } = new();

    public List<TrackerMetricItemSummary> TopUploadTrackers { get; set; } = new();

    public List<TrackerMetricItemSummary> TopPeerTrackers { get; set; } = new();

    public List<HourlyTrafficPoint> HourlyHistory { get; set; } = new();
}

public class TrackerMetricItemSummary
{
    public int Id { get; set; }

    public string TrackerUrl { get; set; }

    public string Domain { get; set; }

    public string Protocol { get; set; }

    public string Status { get; set; }

    public long TotalUploaded { get; set; }

    public long TotalDownloaded { get; set; }

    public long TotalPeersDiscovered { get; set; }

    public double AvgResponseTimeMs { get; set; }

    public double SuccessRate { get; set; }
}

public class HourlyTrafficPoint
{
    public string TimeLabel { get; set; }

    public DateTime Timestamp { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public int Announces { get; set; }

    public int PeersDiscovered { get; set; }

    public double AvgLatencyMs { get; set; }
}
