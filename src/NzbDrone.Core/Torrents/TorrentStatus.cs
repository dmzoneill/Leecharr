// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Torrents;

public enum TorrentStatus
{
    Queued = 0,
    Checking = 1,
    Downloading = 2,
    Seeding = 3,
    Paused = 4,
    Stopped = 5,
    Error = 6,
    Stalled = 7,
    Completed = 8,
}
