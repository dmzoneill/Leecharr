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

public class TrackerAnnounceResult
{
    public bool Success { get; set; }

    public string FailureReason { get; set; }

    public int Interval { get; set; }

    public int MinInterval { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public IReadOnlyList<TrackerPeerState> Peers { get; set; } = Array.Empty<TrackerPeerState>();
}

public class TrackerScrapeItem
{
    public byte[] InfoHash { get; set; }

    public int Seeders { get; set; }

    public long Downloaded { get; set; }

    public int Leechers { get; set; }
}

public class TrackerScrapeResult
{
    public bool Success { get; set; }

    public string FailureReason { get; set; }

    public IReadOnlyList<TrackerScrapeItem> Files { get; set; } = Array.Empty<TrackerScrapeItem>();
}

public interface IEmbeddedTrackerService
{
    bool IsEnabled { get; }

    int ActiveSwarmsCount { get; }

    int ActivePeersCount { get; }

    int MaxSwarms { get; set; }

    byte[] ProcessAnnounce(TrackerAnnounceRequest request);

    byte[] ProcessScrape(List<byte[]> infoHashList);

    TrackerAnnounceResult Announce(TrackerAnnounceRequest request);

    TrackerScrapeResult Scrape(List<byte[]> infoHashList);

    void RegisterSwarm(string infoHashHex);

    void UnregisterSwarm(string infoHashHex);

    void PruneInactivePeers();

    void PruneInactivePeers(TimeSpan timeout);
}
