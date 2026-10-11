using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class DhtPolicyTest
{
    [Test]
    public void GetMaxInboundQueriesPerSecond_WhenRateLimitDisabled_ReturnsUnlimited()
    {
        var config = Substitute.For<IConfigService>();
        config.DhtRateLimitEnabled.Returns(false);

        DhtPolicy.GetMaxInboundQueriesPerSecond(config).Should().Be(int.MaxValue);
    }

    [Test]
    public void GetBucketSize_ClampsToSupportedRange()
    {
        var config = Substitute.For<IConfigService>();
        config.DhtBucketSize.Returns(2);

        DhtPolicy.GetBucketSize(config).Should().Be(4);

        config.DhtBucketSize.Returns(64);
        DhtPolicy.GetBucketSize(config).Should().Be(32);
    }

    [Test]
    public void GetMinimumAnnouncementInterval_IsBelowConfiguredAnnounceInterval()
    {
        var config = Substitute.For<IConfigService>();
        config.DhtAnnouncementInterval.Returns(1800);

        DhtPolicy.GetMinimumAnnouncementInterval(config).TotalSeconds.Should().BeLessThan(DhtPolicy.GetAnnouncementInterval(config).TotalSeconds);
    }
}
