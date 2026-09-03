// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;

namespace NzbDrone.Core.BitTorrent.Tracker;

public class TrackerAnnounceRequest
{
    public byte[] InfoHashBytes { get; set; }

    public string InfoHashHex { get; set; }

    public byte[] PeerIdBytes { get; set; }

    public string PeerId { get; set; }

    public IPAddress RemoteIp { get; set; }

    public int Port { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public long Left { get; set; }

    public string Event { get; set; }

    public bool Compact { get; set; } = true;

    public int NumWant { get; set; } = 50;
}

public interface IEmbeddedTrackerService
{
    bool IsEnabled { get; }

    int ActiveSwarmsCount { get; }

    int ActivePeersCount { get; }

    int MaxSwarms { get; set; }

    byte[] ProcessAnnounce(TrackerAnnounceRequest request);

    byte[] ProcessScrape(List<byte[]> infoHashList);

    void RegisterSwarm(string infoHashHex);

    void UnregisterSwarm(string infoHashHex);

    void PruneInactivePeers();

    void PruneInactivePeers(TimeSpan timeout);
}
