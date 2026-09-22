// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.SignalR;

public class TorrentSnapshotResource
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string Status { get; set; }

    public double Progress { get; set; }

    public long DownloadSpeed { get; set; }

    public long UploadSpeed { get; set; }

    public long Eta { get; set; }

    public long Size { get; set; }

    public long TotalSize { get; set; }

    public bool Active { get; set; }
}

public class StateSnapshotResource
{
    public List<TorrentSnapshotResource> Torrents { get; set; } = new();

    public long DownloadSpeed { get; set; }

    public long UploadSpeed { get; set; }

    public int ActiveCount { get; set; }

    public int TotalCount { get; set; }

    public DateTime TimestampUtc { get; set; }
}
