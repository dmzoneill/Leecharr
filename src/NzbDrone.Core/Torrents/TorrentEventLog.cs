// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Torrents;

public class TorrentEventLog
{
    public int Id { get; set; }

    public int TorrentId { get; set; }

    public string Level { get; set; } = "Info";

    public string Source { get; set; } = "Engine";

    public string Message { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
