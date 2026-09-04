// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentFile : ModelBase
{
    public int TorrentId { get; set; }

    public string Path { get; set; }

    public long Size { get; set; }

    public int PieceOffset { get; set; }

    public int PieceCount { get; set; }

    public int Priority { get; set; }

    public double Progress { get; set; }

    [Ignore]
    public long BytesCompleted { get; set; }
}
