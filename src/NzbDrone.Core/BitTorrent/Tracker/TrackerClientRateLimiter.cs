// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;

namespace NzbDrone.Core.BitTorrent.Tracker;

public class TrackerClientRateLimiter : ITrackerClientRateLimiter
{
    private const int MaxClientRateLimitEntries = 10000;

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
        this.PruneStaleWindowsIfNeeded(currentMinute);

        if (!this.clientRateLimits.ContainsKey(clientIp) && this.clientRateLimits.Count >= MaxClientRateLimitEntries)
        {
            return true;
        }

        var entry = this.clientRateLimits.AddOrUpdate(
            clientIp,
            _ => (1, currentMinute),
            (_, existing) => existing.WindowMinute == currentMinute ? (existing.Count + 1, currentMinute) : (1, currentMinute));

        return entry.Count > limitPerMinute;
    }

    private void PruneStaleWindowsIfNeeded(long currentMinute)
    {
        var now = DateTime.UtcNow;
        if (now - this.lastPruneUtc < TimeSpan.FromSeconds(30) && this.clientRateLimits.Count < MaxClientRateLimitEntries)
        {
            return;
        }

        lock (this.pruneLock)
        {
            if (now - this.lastPruneUtc < TimeSpan.FromSeconds(30) && this.clientRateLimits.Count < MaxClientRateLimitEntries)
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

            if (this.clientRateLimits.Count >= MaxClientRateLimitEntries)
            {
                var excess = this.clientRateLimits.Count - MaxClientRateLimitEntries + 1000;
                var toEvict = this.clientRateLimits.OrderBy(kvp => kvp.Value.WindowMinute).Take(excess);
                foreach (var item in toEvict)
                {
                    this.clientRateLimits.TryRemove(item.Key, out _);
                }
            }
        }
    }
}
