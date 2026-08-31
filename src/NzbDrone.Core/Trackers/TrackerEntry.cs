// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers;

public class TrackerEntry : ModelBase
{
    public int TorrentId { get; set; }

    public string Url { get; set; }

    public int Tier { get; set; }

    public int Status { get; set; }

    public bool Enabled { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int Downloaded { get; set; }

    public int TotalAnnounces { get; set; }

    public int SuccessfulAnnounces { get; set; }

    public int ConsecutiveFailures { get; set; }

    public long LastResponseTime { get; set; }

    public int AnnounceInterval { get; set; }

    public DateTime? LastAnnounce { get; set; }

    public DateTime? NextAnnounce { get; set; }

    public string ErrorMessage { get; set; }
}
