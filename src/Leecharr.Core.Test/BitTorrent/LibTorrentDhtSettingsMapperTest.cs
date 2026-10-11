using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class LibTorrentDhtSettingsMapperTest
{
    [Test]
    public void ApplyToSessionArgs_WritesProtocolDhtTuningKeys()
    {
        var config = Substitute.For<IConfigService>();
        config.DhtRoutingTableSize.Returns(500);
        config.DhtAnnouncementInterval.Returns(900);
        config.DhtBootstrapTimeout.Returns(45);
        config.DhtQueryTimeout.Returns(20);
        config.DhtMaxNodes.Returns(2500);
        config.DhtBucketSize.Returns(16);
        config.DhtConcurrentQueries.Returns(6);
        config.DhtAutoBootstrap.Returns(true);
        config.DhtRateLimitEnabled.Returns(true);
        config.DhtMaxQueriesPerSecond.Returns(40);

        var sessionArgs = new Dictionary<string, object>();
        LibTorrentDhtSettingsMapper.ApplyToSessionArgs(config, sessionArgs);

        sessionArgs["dht_routing_table_size"].Should().Be(500);
        sessionArgs["dht_announcement_interval"].Should().Be(900);
        sessionArgs["dht_bootstrap_timeout"].Should().Be(45);
        sessionArgs["dht_query_timeout"].Should().Be(20);
        sessionArgs["dht_max_nodes"].Should().Be(2500);
        sessionArgs["dht_bucket_size"].Should().Be(16);
        sessionArgs["dht_concurrent_queries"].Should().Be(6);
        sessionArgs["dht_auto_bootstrap"].Should().Be(true);
        sessionArgs["dht_rate_limit_enabled"].Should().Be(true);
        sessionArgs["dht_max_queries_per_second"].Should().Be(40);
    }
}
