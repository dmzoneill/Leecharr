// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentEventLog : ModelBase
{
    public int TorrentId { get; set; }

    public string Level { get; set; } = "Info";

    public string Source { get; set; } = "Engine";

    public string Message { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
