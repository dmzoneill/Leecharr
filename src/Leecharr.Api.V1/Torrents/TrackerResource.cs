// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Torrents;

public class TrackerResource : RestResource
{
    public string Url { get; set; }

    public string Status { get; set; } = "Working";

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int Downloaded { get; set; }

    public int Tier { get; set; }

    public int AnnounceInterval { get; set; } = 1800;

    public int NextAnnounceSeconds { get; set; } = 1800;

    public int TotalAnnounces { get; set; }

    public int SuccessfulAnnounces { get; set; }

    public DateTime? LastAnnounce { get; set; }

    public DateTime? NextAnnounce { get; set; }

    public string Message { get; set; }
}

public class AddTrackerRequest
{
    public string Url { get; set; }
}

public class TorrentEventLogResource : RestResource
{
    public int TorrentId { get; set; }

    public string Level { get; set; } = "Info";

    public string Source { get; set; } = "Engine";

    public string Message { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
