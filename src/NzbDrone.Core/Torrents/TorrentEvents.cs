// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class TorrentAddedEvent : IEvent
{
    public Torrent Torrent { get; set; }
}

public class TorrentUpdatedEvent : IEvent
{
    public Torrent Torrent { get; set; }
}

public class TorrentDeletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public bool DeleteFiles { get; set; }
}

public class TorrentStatusChangedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentStatus OldStatus { get; set; }

    public TorrentStatus NewStatus { get; set; }
}

public class TorrentDownloadCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentDownloadCompletedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentSeedGoalReachedEvent : IEvent
{
    public Torrent Torrent { get; }

    public TorrentSeedGoalReachedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentMetadataReceivedEvent : IEvent
{
    public int TorrentId { get; set; }

    public string InfoHash { get; set; }

    public long TotalSize { get; set; }

    public int PieceCount { get; set; }

    public int PieceLength { get; set; }

    public string Name { get; set; }

    public IReadOnlyList<TorrentFile> Files { get; set; }

    public byte[] TorrentBytes { get; set; }
}

public class HealthIssueEvent : IEvent
{
    public Torrent Torrent { get; }

    public int TorrentId { get; }

    public string Source { get; }

    public string Message { get; }

    public bool IsResolved { get; }

    public HealthIssueEvent(Torrent torrent, string source, string message, bool isResolved = false)
    {
        this.Torrent = torrent;
        this.TorrentId = torrent?.Id ?? 0;
        this.Source = source;
        this.Message = message;
        this.IsResolved = isResolved;
    }

    public HealthIssueEvent(int torrentId, string source, string message, bool isResolved = false)
    {
        this.TorrentId = torrentId;
        this.Source = source;
        this.Message = message;
        this.IsResolved = isResolved;
    }
}
