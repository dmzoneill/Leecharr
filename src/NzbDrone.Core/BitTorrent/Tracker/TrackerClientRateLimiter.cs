// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Net;

namespace NzbDrone.Core.BitTorrent.Tracker;

public class TrackerClientRateLimiter : ITrackerClientRateLimiter
{
    private readonly ConcurrentDictionary<IPAddress, (int Count, long WindowMinute)> clientRateLimits = new();
    private readonly object pruneLock = new();
    private DateTime lastPruneUtc = DateTime.MinValue;

    public bool IsRateLimited(IPAddress clientIp, int limitPerMinute)
    {
        if (clientIp == null || limitPerMinute <= 0)
        {
            return false;
        }

        if (clientIp.IsIPv4MappedToIPv6)
        {
            clientIp = clientIp.MapToIPv4();
        }

        var currentMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var entry = this.clientRateLimits.AddOrUpdate(
            clientIp,
            _ => (1, currentMinute),
            (_, existing) => existing.WindowMinute == currentMinute ? (existing.Count + 1, currentMinute) : (1, currentMinute));

        this.PruneStaleWindowsIfNeeded(currentMinute);
        return entry.Count > limitPerMinute;
    }

    private void PruneStaleWindowsIfNeeded(long currentMinute)
    {
        var now = DateTime.UtcNow;
        lock (this.pruneLock)
        {
            if (now - this.lastPruneUtc < TimeSpan.FromSeconds(30))
            {
                return;
            }

            this.lastPruneUtc = now;
            foreach (var kvp in this.clientRateLimits)
            {
                if (kvp.Value.WindowMinute < currentMinute)
                {
                    this.clientRateLimits.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
