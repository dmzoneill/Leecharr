// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.BitTorrent;

public class TorrentEngineSwitchedEvent : IEvent
{
    public string PreviousEngine { get; }

    public string PreviousVersion { get; }

    public string NewEngine { get; }

    public string NewVersion { get; }

    public int TorrentsMigrated { get; }

    public TorrentEngineSwitchedEvent(string previousEngine, string newEngine, int torrentsMigrated)
        : this(previousEngine, string.Empty, newEngine, string.Empty, torrentsMigrated)
    {
    }

    public TorrentEngineSwitchedEvent(string previousEngine, string previousVersion, string newEngine, string newVersion, int torrentsMigrated)
    {
        this.PreviousEngine = previousEngine;
        this.PreviousVersion = previousVersion;
        this.NewEngine = newEngine;
        this.NewVersion = newVersion;
        this.TorrentsMigrated = torrentsMigrated;
    }
}
