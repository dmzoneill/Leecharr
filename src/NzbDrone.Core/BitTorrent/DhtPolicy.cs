using System;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public static class DhtPolicy
{
    public static int GetBucketSize(IConfigService config)
    {
        return Math.Clamp(config?.DhtBucketSize ?? 8, 4, 32);
    }

    public static int GetRoutingTableBucketCount(IConfigService config)
    {
        return Math.Clamp(config?.DhtRoutingTableSize ?? 200, 10, 1000);
    }

    public static int GetMaxRoutingNodes(IConfigService config)
    {
        return Math.Clamp(config?.DhtMaxNodes ?? 1000, 100, 10000);
    }

    public static TimeSpan GetAnnouncementInterval(IConfigService config)
    {
        var seconds = Math.Max(60, config?.DhtAnnouncementInterval ?? 1800);
        return TimeSpan.FromSeconds(seconds);
    }

    public static TimeSpan GetMinimumAnnouncementInterval(IConfigService config)
    {
        var interval = GetAnnouncementInterval(config);
        var minimumSeconds = Math.Max(180, (int)Math.Min(interval.TotalSeconds / 2, 900));
        return TimeSpan.FromSeconds(minimumSeconds);
    }

    public static TimeSpan GetBootstrapTimeout(IConfigService config)
    {
        var seconds = Math.Max(1, config?.DhtBootstrapTimeout ?? 30);
        return TimeSpan.FromSeconds(seconds);
    }

    public static TimeSpan GetQueryTimeout(IConfigService config)
    {
        var seconds = Math.Max(1, config?.DhtQueryTimeout ?? 15);
        return TimeSpan.FromSeconds(seconds);
    }

    public static int GetConcurrentQueries(IConfigService config)
    {
        return Math.Clamp(config?.DhtConcurrentQueries ?? 4, 1, 16);
    }

    public static bool ShouldAutoBootstrap(IConfigService config)
    {
        return config?.DhtAutoBootstrap ?? true;
    }

    public static bool IsRateLimitEnabled(IConfigService config)
    {
        return config?.DhtRateLimitEnabled ?? true;
    }

    public static int GetMaxInboundQueriesPerSecond(IConfigService config)
    {
        if (config == null || !config.DhtRateLimitEnabled)
        {
            return int.MaxValue;
        }

        return Math.Clamp(config.DhtMaxQueriesPerSecond, 5, 500);
    }
}
