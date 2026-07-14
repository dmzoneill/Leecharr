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
        Torrent = torrent;
    }
}
