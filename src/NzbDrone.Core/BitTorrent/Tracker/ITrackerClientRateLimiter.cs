// Copyright (c) FeedItOut. All rights reserved.

using System.Net;

namespace NzbDrone.Core.BitTorrent.Tracker;

public interface ITrackerClientRateLimiter
{
    bool IsRateLimited(IPAddress clientIp, int limitPerMinute);
}
