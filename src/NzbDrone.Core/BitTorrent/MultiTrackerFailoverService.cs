using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MonoTorrent.Trackers;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public interface IMultiTrackerFailoverService
{
    bool ShouldSkipAnnounce(int torrentId, int tierIndex, Uri trackerUri, DateTime utcNow);

    ITracker SelectTrackerForTier(int torrentId, int tierIndex, TrackerTier tier, DateTime utcNow);

    void RecordAnnounceOutcome(int torrentId, int tierIndex, ITracker tracker, bool success, DateTime utcNow);

    void ClearTorrent(int torrentId);
}

public sealed class MultiTrackerFailoverService : IMultiTrackerFailoverService
{
    private readonly IConfigService configService;
    private readonly Func<DateTime> utcNowProvider;
    private readonly ConcurrentDictionary<int, TorrentFailoverState> torrentStates = new();

    public MultiTrackerFailoverService(IConfigService configService, Func<DateTime> utcNowProvider = null)
    {
        this.configService = configService;
        this.utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public bool ShouldSkipAnnounce(int torrentId, int tierIndex, Uri trackerUri, DateTime utcNow)
    {
        if (!MultiTrackerAnnouncePolicy.IsFailoverEnabled(this.configService) || trackerUri == null)
        {
            return false;
        }

        var state = this.GetTorrentState(torrentId);
        var trackerState = state.GetTrackerState(tierIndex, trackerUri);
        return trackerState.BackoffUntilUtc.HasValue && utcNow < trackerState.BackoffUntilUtc.Value;
    }

    public ITracker SelectTrackerForTier(int torrentId, int tierIndex, TrackerTier tier, DateTime utcNow)
    {
        if (tier?.Trackers == null || tier.Trackers.Count == 0)
        {
            return null;
        }

        if (!MultiTrackerAnnouncePolicy.IsFailoverEnabled(this.configService))
        {
            return tier.ActiveTracker ?? tier.Trackers.FirstOrDefault();
        }

        var trackers = tier.Trackers.ToList();
        var state = this.GetTorrentState(torrentId);
        var tierState = state.GetTierState(tierIndex);
        var startIndex = tierState.ActiveTrackerIndex % trackers.Count;

        for (var offset = 0; offset < trackers.Count; offset++)
        {
            var index = (startIndex + offset) % trackers.Count;
            var candidate = trackers[index];
            if (candidate?.Uri == null)
            {
                continue;
            }

            if (!this.ShouldSkipAnnounce(torrentId, tierIndex, candidate.Uri, utcNow))
            {
                tierState.ActiveTrackerIndex = index;
                return candidate;
            }
        }

        return tier.ActiveTracker ?? trackers.FirstOrDefault();
    }

    public void RecordAnnounceOutcome(int torrentId, int tierIndex, ITracker tracker, bool success, DateTime utcNow)
    {
        if (tracker?.Uri == null || !MultiTrackerAnnouncePolicy.IsFailoverEnabled(this.configService))
        {
            return;
        }

        var state = this.GetTorrentState(torrentId);
        var trackerState = state.GetTrackerState(tierIndex, tracker.Uri);
        var tierState = state.GetTierState(tierIndex);
        var maxFailures = Math.Max(1, this.configService.FailoverMaxConsecutiveFailures);
        var baseBackoff = Math.Max(1, this.configService.FailoverBackoffBaseSeconds);
        var maxBackoff = Math.Max(baseBackoff, this.configService.FailoverMaxBackoffSeconds);

        if (success)
        {
            trackerState.ConsecutiveFailures = 0;
            trackerState.BackoffUntilUtc = null;
            return;
        }

        trackerState.ConsecutiveFailures++;
        var backoffSeconds = MultiTrackerAnnouncePolicy.ComputeBackoffSeconds(
            trackerState.ConsecutiveFailures,
            baseBackoff,
            maxBackoff);
        trackerState.BackoffUntilUtc = utcNow.AddSeconds(backoffSeconds);

        if (trackerState.ConsecutiveFailures >= maxFailures)
        {
            trackerState.ConsecutiveFailures = 0;
            trackerState.BackoffUntilUtc = utcNow.AddSeconds(baseBackoff);
            tierState.ActiveTrackerIndex++;
        }
    }

    public void ClearTorrent(int torrentId)
    {
        this.torrentStates.TryRemove(torrentId, out _);
    }

    private TorrentFailoverState GetTorrentState(int torrentId)
    {
        return this.torrentStates.GetOrAdd(torrentId, _ => new TorrentFailoverState());
    }

    private sealed class TorrentFailoverState
    {
        private readonly ConcurrentDictionary<(int TierIndex, string TrackerUri), TrackerFailoverState> trackerStates = new();
        private readonly ConcurrentDictionary<int, TierFailoverState> tierStates = new();

        public TrackerFailoverState GetTrackerState(int tierIndex, Uri trackerUri)
        {
            var key = (tierIndex, trackerUri.ToString());
            return this.trackerStates.GetOrAdd(key, _ => new TrackerFailoverState());
        }

        public TierFailoverState GetTierState(int tierIndex)
        {
            return this.tierStates.GetOrAdd(tierIndex, _ => new TierFailoverState());
        }
    }

    private sealed class TrackerFailoverState
    {
        public int ConsecutiveFailures { get; set; }

        public DateTime? BackoffUntilUtc { get; set; }
    }

    private sealed class TierFailoverState
    {
        public int ActiveTrackerIndex { get; set; }
    }
}
