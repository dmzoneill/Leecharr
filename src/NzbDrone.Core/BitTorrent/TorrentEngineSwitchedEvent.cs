using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.BitTorrent;

public class TorrentEngineSwitchedEvent : IEvent
{
    public string PreviousEngine { get; }
    public string NewEngine { get; }
    public int TorrentsMigrated { get; }

    public TorrentEngineSwitchedEvent(string previousEngine, string newEngine, int torrentsMigrated)
    {
        PreviousEngine = previousEngine;
        NewEngine = newEngine;
        TorrentsMigrated = torrentsMigrated;
    }
}
