// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.BitTorrent;

public class TorrentEngineSwitchedEvent : IEvent
{
    public string PreviousEngine { get; }

    public string NewEngine { get; }

    public int TorrentsMigrated { get; }

    public TorrentEngineSwitchedEvent(string previousEngine, string newEngine, int torrentsMigrated)
    {
        this.PreviousEngine = previousEngine;
        this.NewEngine = newEngine;
        this.TorrentsMigrated = torrentsMigrated;
    }
}
