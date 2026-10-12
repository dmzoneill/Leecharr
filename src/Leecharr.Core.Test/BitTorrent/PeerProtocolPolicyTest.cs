using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class PeerProtocolPolicyTest
{
    [Test]
    public void GetHandshakeTimeout_UsesConfigValueWithMinimumOneSecond()
    {
        var config = Substitute.For<IConfigService>();
        config.HandshakeTimeoutSeconds.Returns(12);

        PeerProtocolPolicy.GetHandshakeTimeout(config).Should().Be(TimeSpan.FromSeconds(12));

        config.HandshakeTimeoutSeconds.Returns(0);
        PeerProtocolPolicy.GetHandshakeTimeout(config).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Test]
    public void GetHandshakeTimeout_DefaultsToThirtySecondsWhenConfigMissing()
    {
        PeerProtocolPolicy.GetHandshakeTimeout(null).Should().Be(TimeSpan.FromSeconds(30));
    }
}
