using System.Collections.Generic;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

internal static class LibTorrentDhtSettingsMapper
{
    public static void ApplyToSessionArgs(IConfigService config, IDictionary<string, object> sessionArgs)
    {
        if (config == null || sessionArgs == null)
        {
            return;
        }

        sessionArgs["dht_routing_table_size"] = DhtPolicy.GetRoutingTableBucketCount(config);
        sessionArgs["dht_announcement_interval"] = (int)DhtPolicy.GetAnnouncementInterval(config).TotalSeconds;
        sessionArgs["dht_bootstrap_timeout"] = (int)DhtPolicy.GetBootstrapTimeout(config).TotalSeconds;
        sessionArgs["dht_query_timeout"] = (int)DhtPolicy.GetQueryTimeout(config).TotalSeconds;
        sessionArgs["dht_max_nodes"] = DhtPolicy.GetMaxRoutingNodes(config);
        sessionArgs["dht_bucket_size"] = DhtPolicy.GetBucketSize(config);
        sessionArgs["dht_concurrent_queries"] = DhtPolicy.GetConcurrentQueries(config);
        sessionArgs["dht_auto_bootstrap"] = DhtPolicy.ShouldAutoBootstrap(config);
        sessionArgs["dht_rate_limit_enabled"] = DhtPolicy.IsRateLimitEnabled(config);
        sessionArgs["dht_max_queries_per_second"] = DhtPolicy.GetMaxInboundQueriesPerSecond(config);
    }
}
