// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Trackers.Metrics;

namespace Leecharr.Api.V1.Trackers;

public class TrackerMetricResource
{
    public int Id { get; set; }

    public string TrackerUrl { get; set; }

    public string Host { get; set; }

    public string Domain { get; set; }

    public string Protocol { get; set; }

    public int Port { get; set; }

    public string Status { get; set; }

    public DateTime FirstSeen { get; set; }

    public DateTime? LastAnnounce { get; set; }

    public DateTime? LastScrape { get; set; }

    public DateTime? LastSuccess { get; set; }

    public DateTime? LastErrorTime { get; set; }

    public string LastErrorMessage { get; set; }

    public long TotalAnnounces { get; set; }

    public long SuccessfulAnnounces { get; set; }

    public long FailedAnnounces { get; set; }

    public double AnnounceSuccessRate { get; set; }

    public long TotalScrapes { get; set; }

    public long SuccessfulScrapes { get; set; }

    public long FailedScrapes { get; set; }

    public long TotalUploaded { get; set; }

    public long TotalDownloaded { get; set; }

    public double Ratio { get; set; }

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

    public double LatencyP50Ms { get; set; }

    public double LatencyP95Ms { get; set; }

    public double LatencyP99Ms { get; set; }

    public double P50LatencyMs { get => this.LatencyP50Ms; set => this.LatencyP50Ms = value; }

    public double P95LatencyMs { get => this.LatencyP95Ms; set => this.LatencyP95Ms = value; }

    public double P99LatencyMs { get => this.LatencyP99Ms; set => this.LatencyP99Ms = value; }

    public double P50ResponseTimeMs { get => this.LatencyP50Ms; set => this.LatencyP50Ms = value; }

    public double P95ResponseTimeMs { get => this.LatencyP95Ms; set => this.LatencyP95Ms = value; }

    public double P99ResponseTimeMs { get => this.LatencyP99Ms; set => this.LatencyP99Ms = value; }
}

public static class TrackerMetricResourceMapper
{
    public static TrackerMetricResource ToResource(TrackerMetric model)
    {
        if (model == null)
        {
            return null;
        }

        var successRate = model.TotalAnnounces > 0
            ? Math.Round((double)model.SuccessfulAnnounces / model.TotalAnnounces * 100.0, 1)
            : 100.0;

        var ratio = model.TotalDownloaded > 0
            ? Math.Round((double)model.TotalUploaded / model.TotalDownloaded, 3)
            : (model.TotalUploaded > 0 ? 999.0 : 0.0);

        return new TrackerMetricResource
        {
            Id = model.Id,
            TrackerUrl = model.TrackerUrl,
            Host = model.Host,
            Domain = model.Domain,
            Protocol = model.Protocol,
            Port = model.Port,
            Status = model.Status,
            FirstSeen = model.FirstSeen,
            LastAnnounce = model.LastAnnounce,
            LastScrape = model.LastScrape,
            LastSuccess = model.LastSuccess,
            LastErrorTime = model.LastErrorTime,
            LastErrorMessage = model.LastErrorMessage,
            TotalAnnounces = model.TotalAnnounces,
            SuccessfulAnnounces = model.SuccessfulAnnounces,
            FailedAnnounces = model.FailedAnnounces,
            AnnounceSuccessRate = successRate,
            TotalScrapes = model.TotalScrapes,
            SuccessfulScrapes = model.SuccessfulScrapes,
            FailedScrapes = model.FailedScrapes,
            TotalUploaded = model.TotalUploaded,
            TotalDownloaded = model.TotalDownloaded,
            Ratio = ratio,
            TotalLeft = model.TotalLeft,
            SessionUploaded = model.SessionUploaded,
            SessionDownloaded = model.SessionDownloaded,
            TotalTorrentsTracked = model.TotalTorrentsTracked,
            LastSeeders = model.LastSeeders,
            LastLeechers = model.LastLeechers,
            LastPeers = model.LastPeers,
            TotalPeersDiscovered = model.TotalPeersDiscovered,
            AvgResponseTimeMs = model.AvgResponseTimeMs,
            LastResponseTimeMs = model.LastResponseTimeMs,
            MinResponseTimeMs = model.MinResponseTimeMs,
            MaxResponseTimeMs = model.MaxResponseTimeMs,
            ConsecutiveFailures = model.ConsecutiveFailures,
            LatencyP50Ms = model.LatencyP50Ms,
            LatencyP95Ms = model.LatencyP95Ms,
            LatencyP99Ms = model.LatencyP99Ms,
        };
    }
}
