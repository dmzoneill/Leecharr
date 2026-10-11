// Copyright (c) FeedItOut. All rights reserved.

using System.Net;

namespace NzbDrone.Core.BitTorrent.Tracker;

public sealed class EmbeddedTrackerHttpRequest
{
    public string RawQuery { get; init; }

    public IPAddress RemoteIp { get; init; }

    public IPAddress LocalIp { get; init; }

    public string ForwardedFor { get; init; }

    public string RealIp { get; init; }
}

public sealed class EmbeddedTrackerHttpResponse
{
    public bool IsNotFound { get; init; }

    public byte[] Body { get; init; }

    public string ContentType { get; init; } = "text/plain";

    public static EmbeddedTrackerHttpResponse NotFound() => new() { IsNotFound = true };

    public static EmbeddedTrackerHttpResponse Plain(byte[] body) => new() { Body = body };
}

public interface IEmbeddedTrackerHttpProcessor
{
    EmbeddedTrackerHttpResponse ProcessAnnounce(EmbeddedTrackerHttpRequest request);

    EmbeddedTrackerHttpResponse ProcessScrape(EmbeddedTrackerHttpRequest request);
}
