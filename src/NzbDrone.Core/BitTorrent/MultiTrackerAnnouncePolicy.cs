using System;
using MonoTorrent.Trackers;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public enum MultiTrackerAnnounceScope
{
    Default,
    AllTiers,
    AllInTier,
}

public static class MultiTrackerAnnouncePolicy
{
    public static MultiTrackerAnnounceScope ResolveAnnounceScope(IConfigService config)
    {
        if (config == null || !config.MultiTrackerEnabled)
        {
            return MultiTrackerAnnounceScope.Default;
        }

        if (config.AnnounceToAllInTier)
        {
            return MultiTrackerAnnounceScope.AllInTier;
        }

        if (config.AnnounceToAllTiers)
        {
            return MultiTrackerAnnounceScope.AllTiers;
        }

        return MultiTrackerAnnounceScope.Default;
    }

    public static bool IsFailoverEnabled(IConfigService config)
    {
        return config != null && config.MultiTrackerEnabled && config.MultiTrackerFailoverEnabled;
    }

    public static bool IsAnnounceSuccessful(ITracker tracker)
    {
        if (tracker == null)
        {
            return false;
        }

        return tracker.Status == TrackerState.Ok && string.IsNullOrWhiteSpace(tracker.FailureMessage);
    }

    public static int ComputeBackoffSeconds(int consecutiveFailures, int baseSeconds, int maxSeconds)
    {
        if (consecutiveFailures <= 0 || baseSeconds <= 0)
        {
            return 0;
        }

        var cappedBase = Math.Max(1, baseSeconds);
        var cappedMax = Math.Max(cappedBase, maxSeconds);
        var exponent = Math.Min(consecutiveFailures - 1, 10);
        var delay = cappedBase * (1 << exponent);
        return Math.Min(delay, cappedMax);
    }
}
