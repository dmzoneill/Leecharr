using System;
using System.Collections.Generic;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Peers;

public class PeerConnectionLogResource : RestResource
{
    public string InfoHash { get; set; }
    public string TorrentName { get; set; }
    public string RemoteIp { get; set; }
    public int RemotePort { get; set; }
    public string PeerId { get; set; }
    public bool IsEncrypted { get; set; }
    public string EventType { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PeerGraphResource
{
    public List<PeerGraphNode> Nodes { get; set; } = new();
    public List<PeerGraphLink> Links { get; set; } = new();
}

public class PeerGraphNode
{
    public string Id { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }
    public string InfoHash { get; set; }
    public bool IsEncrypted { get; set; }
}

public class PeerGraphLink
{
    public string Source { get; set; }
    public string Target { get; set; }
    public string Type { get; set; }
}
