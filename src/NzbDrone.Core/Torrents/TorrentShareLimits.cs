// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Torrents;

public static class TorrentShareLimits
{
    public const double ApiUseGlobalRatio = -2;
    public const double ApiNoRatioLimit = -1;
    public const int ApiUseGlobalMinutes = -2;
    public const int ApiNoMinuteLimit = -1;

    public static void ApplyApiRatioLimit(Torrent torrent, double apiLimit)
    {
        if (apiLimit >= 0)
        {
            torrent.TargetRatioLimitKind = TorrentShareLimitKind.Explicit;
            torrent.TargetRatio = apiLimit;
            return;
        }

        if (apiLimit <= ApiNoRatioLimit)
        {
            torrent.TargetRatioLimitKind = TorrentShareLimitKind.Unlimited;
            torrent.TargetRatio = 0;
            return;
        }

        torrent.TargetRatioLimitKind = TorrentShareLimitKind.Global;
        torrent.TargetRatio = 0;
    }

    public static void ApplyApiSeedTimeLimit(Torrent torrent, int apiLimitMinutes)
    {
        if (apiLimitMinutes >= 0)
        {
            torrent.TargetSeedTimeLimitKind = TorrentShareLimitKind.Explicit;
            torrent.TargetSeedTimeMinutes = apiLimitMinutes;
            return;
        }

        if (apiLimitMinutes <= ApiNoMinuteLimit)
        {
            torrent.TargetSeedTimeLimitKind = TorrentShareLimitKind.Unlimited;
            torrent.TargetSeedTimeMinutes = 0;
            return;
        }

        torrent.TargetSeedTimeLimitKind = TorrentShareLimitKind.Global;
        torrent.TargetSeedTimeMinutes = 0;
    }

    public static void ApplyApiInactiveSeedTimeLimit(Torrent torrent, int apiLimitMinutes)
    {
        if (apiLimitMinutes >= 0)
        {
            torrent.InactiveSeedingTimeLimitKind = TorrentShareLimitKind.Explicit;
            torrent.InactiveSeedingTimeLimitMinutes = apiLimitMinutes;
            return;
        }

        if (apiLimitMinutes <= ApiNoMinuteLimit)
        {
            torrent.InactiveSeedingTimeLimitKind = TorrentShareLimitKind.Unlimited;
            torrent.InactiveSeedingTimeLimitMinutes = 0;
            return;
        }

        torrent.InactiveSeedingTimeLimitKind = TorrentShareLimitKind.Global;
        torrent.InactiveSeedingTimeLimitMinutes = 0;
    }

    public static double ToApiRatioLimit(Torrent torrent)
    {
        if (torrent == null)
        {
            return ApiUseGlobalRatio;
        }

        return torrent.TargetRatioLimitKind switch
        {
            TorrentShareLimitKind.Unlimited => ApiNoRatioLimit,
            TorrentShareLimitKind.Global => ApiUseGlobalRatio,
            TorrentShareLimitKind.Explicit => torrent.TargetRatio,
            _ => torrent.TargetRatio > 0 ? torrent.TargetRatio : ApiUseGlobalRatio,
        };
    }

    public static int ToApiSeedTimeLimitMinutes(Torrent torrent)
    {
        if (torrent == null)
        {
            return ApiUseGlobalMinutes;
        }

        return torrent.TargetSeedTimeLimitKind switch
        {
            TorrentShareLimitKind.Unlimited => ApiNoMinuteLimit,
            TorrentShareLimitKind.Global => ApiUseGlobalMinutes,
            TorrentShareLimitKind.Explicit => torrent.TargetSeedTimeMinutes,
            _ => torrent.TargetSeedTimeMinutes > 0 ? torrent.TargetSeedTimeMinutes : ApiUseGlobalMinutes,
        };
    }

    public static int ToApiInactiveSeedTimeLimitMinutes(Torrent torrent)
    {
        if (torrent == null)
        {
            return ApiUseGlobalMinutes;
        }

        return torrent.InactiveSeedingTimeLimitKind switch
        {
            TorrentShareLimitKind.Unlimited => ApiNoMinuteLimit,
            TorrentShareLimitKind.Global => ApiUseGlobalMinutes,
            TorrentShareLimitKind.Explicit => torrent.InactiveSeedingTimeLimitMinutes,
            _ => torrent.InactiveSeedingTimeLimitMinutes > 0 ? torrent.InactiveSeedingTimeLimitMinutes : ApiUseGlobalMinutes,
        };
    }

    public static bool HasExplicitTorrentRatio(Torrent torrent)
    {
        return torrent != null &&
               (torrent.TargetRatioLimitKind == TorrentShareLimitKind.Explicit ||
                (torrent.TargetRatioLimitKind == TorrentShareLimitKind.Default && torrent.TargetRatio > 0));
    }

    public static bool HasExplicitTorrentSeedTime(Torrent torrent)
    {
        return torrent != null &&
               (torrent.TargetSeedTimeLimitKind == TorrentShareLimitKind.Explicit ||
                (torrent.TargetSeedTimeLimitKind == TorrentShareLimitKind.Default && torrent.TargetSeedTimeMinutes > 0));
    }
}
