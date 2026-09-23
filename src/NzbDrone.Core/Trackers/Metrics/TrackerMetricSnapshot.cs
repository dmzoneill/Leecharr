// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers.Metrics;

public class TrackerMetricSnapshot : ModelBase
{
    public int TrackerMetricId { get; set; }

    public string TrackerUrl { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public long ResponseTimeMs { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int PeersDiscovered { get; set; }

    public bool IsSuccess { get; set; } = true;

    public string Operation { get; set; } = "Announce";
}
