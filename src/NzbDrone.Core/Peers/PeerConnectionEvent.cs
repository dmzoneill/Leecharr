// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Peers;

public class PeerConnectionEvent
{
    public long Id { get; set; }

    public string InfoHash { get; set; }

    public string TorrentName { get; set; }

    public string RemoteIp { get; set; }

    public int RemotePort { get; set; }

    public string PeerId { get; set; }

    public bool IsEncrypted { get; set; }

    public string CountryCode { get; set; }

    public string CountryName { get; set; }

    public string City { get; set; }

    public string EventType { get; set; } = "Connected";

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
