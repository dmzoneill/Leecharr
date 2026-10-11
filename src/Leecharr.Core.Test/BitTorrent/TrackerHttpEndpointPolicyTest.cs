// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class TrackerHttpEndpointPolicyTest
{
    [Test]
    public void ShouldAcceptOnLocalPort_WhenHttpDisabled_ReturnsFalse()
    {
        var config = Substitute.For<IConfigService>();
        config.TrackerServerEnabled.Returns(true);
        config.TrackerHttpEnabled.Returns(false);
        config.TrackerHttpPort.Returns(6969);

        TrackerHttpEndpointPolicy.ShouldAcceptOnLocalPort(config, 6969).Should().BeFalse();
    }

    [Test]
    public void ShouldRunDedicatedListener_WhenPortsDiffer_ReturnsTrue()
    {
        var config = Substitute.For<IConfigService>();
        config.TrackerServerEnabled.Returns(true);
        config.TrackerHttpEnabled.Returns(true);
        config.TrackerHttpPort.Returns(6969);

        var configFile = Substitute.For<IConfigFileProvider>();
        configFile.Port.Returns(7889);

        TrackerHttpEndpointPolicy.ShouldRunDedicatedListener(config, configFile).Should().BeTrue();
    }

    [Test]
    public void ShouldRunDedicatedListener_WhenPortsMatch_ReturnsFalse()
    {
        var config = Substitute.For<IConfigService>();
        config.TrackerServerEnabled.Returns(true);
        config.TrackerHttpEnabled.Returns(true);
        config.TrackerHttpPort.Returns(7889);

        var configFile = Substitute.For<IConfigFileProvider>();
        configFile.Port.Returns(7889);

        TrackerHttpEndpointPolicy.ShouldRunDedicatedListener(config, configFile).Should().BeFalse();
    }
}
