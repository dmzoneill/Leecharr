// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Trackers.Metrics;

public class HourlyTrackerMetricPoint
{
    public string Bucket { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public int Announces { get; set; }

    public int PeersDiscovered { get; set; }

    public double AvgLatencyMs { get; set; }
}
