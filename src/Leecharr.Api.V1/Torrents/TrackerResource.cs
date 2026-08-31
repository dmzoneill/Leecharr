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

    public DateTime? LastAnnounce { get; set; }

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

    public string Message { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
