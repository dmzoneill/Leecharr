// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace NzbDrone.Core.TrackerBoost;

public interface ITrackerBoostStateStore
{
    DateTime? LastScanTime { get; set; }

    DateTime? LastHarvestTime { get; set; }

    DateTime? LastProwlarrHarvestTime { get; set; }

    DateTime? LastAutoBoostTime { get; set; }

    int TotalTorrentsBoosted { get; set; }

    int TotalTrackersInjected { get; set; }

    int TotalVerifiedMatchesCount { get; set; }

    ConcurrentDictionary<string, (DateTime BoostedAt, HashSet<string> InjectedTrackers)> BoostHistory { get; }

    ConcurrentDictionary<string, (IPAddress[] Addresses, DateTime ExpiresUtc)> DnsCache { get; }

    ConcurrentQueue<TrackerBoostLogEntry> LogBuffer { get; }

    int NextLogId();

    void IncrementTorrentsBoosted(int count = 1);

    void IncrementTrackersInjected(int count = 1);

    void IncrementVerifiedMatches(int count = 1);

    void Reset();
}

public class TrackerBoostStateStore : ITrackerBoostStateStore
{
    private int totalTorrentsBoosted;
    private int totalTrackersInjected;
    private int totalVerifiedMatchesCount;
    private int nextLogId;

    public static TrackerBoostStateStore Shared { get; } = new();

    public DateTime? LastScanTime { get; set; }

    public DateTime? LastHarvestTime { get; set; }

    public DateTime? LastProwlarrHarvestTime { get; set; }

    public DateTime? LastAutoBoostTime { get; set; }

    public int TotalTorrentsBoosted
    {
        get => Volatile.Read(ref this.totalTorrentsBoosted);
        set => Volatile.Write(ref this.totalTorrentsBoosted, value);
    }

    public int TotalTrackersInjected
    {
        get => Volatile.Read(ref this.totalTrackersInjected);
        set => Volatile.Write(ref this.totalTrackersInjected, value);
    }

    public int TotalVerifiedMatchesCount
    {
        get => Volatile.Read(ref this.totalVerifiedMatchesCount);
        set => Volatile.Write(ref this.totalVerifiedMatchesCount, value);
    }

    public ConcurrentDictionary<string, (DateTime BoostedAt, HashSet<string> InjectedTrackers)> BoostHistory { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, (IPAddress[] Addresses, DateTime ExpiresUtc)> DnsCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<TrackerBoostLogEntry> LogBuffer { get; } = new();

    public int NextLogId()
    {
        return Interlocked.Increment(ref this.nextLogId);
    }

    public void IncrementTorrentsBoosted(int count = 1)
    {
        Interlocked.Add(ref this.totalTorrentsBoosted, count);
    }

    public void IncrementTrackersInjected(int count = 1)
    {
        Interlocked.Add(ref this.totalTrackersInjected, count);
    }

    public void IncrementVerifiedMatches(int count = 1)
    {
        Interlocked.Add(ref this.totalVerifiedMatchesCount, count);
    }

    public void Reset()
    {
        this.LastScanTime = null;
        this.LastHarvestTime = null;
        this.LastProwlarrHarvestTime = null;
        this.LastAutoBoostTime = null;
        this.TotalTorrentsBoosted = 0;
        this.TotalTrackersInjected = 0;
        this.TotalVerifiedMatchesCount = 0;
        this.nextLogId = 0;
        this.BoostHistory.Clear();
        this.DnsCache.Clear();
        this.LogBuffer.Clear();
    }
}
