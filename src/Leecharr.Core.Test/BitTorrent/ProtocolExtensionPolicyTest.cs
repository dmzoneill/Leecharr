using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class ProtocolExtensionPolicyTest
{
    [Test]
    public void IsUtPexEnabled_RequiresEnablePexAndExtensionUtPex()
    {
        var config = Substitute.For<IConfigService>();
        config.EnablePex.Returns(true);
        config.ExtensionUtPex.Returns(false);

        ProtocolExtensionPolicy.IsUtPexEnabled(config).Should().BeFalse();

        config.ExtensionUtPex.Returns(true);
        ProtocolExtensionPolicy.IsUtPexEnabled(config).Should().BeTrue();

        config.EnablePex.Returns(false);
        ProtocolExtensionPolicy.IsUtPexEnabled(config).Should().BeFalse();
    }

    [Test]
    public void AllowPeerExchange_IsFalseForPrivateTorrents()
    {
        var config = Substitute.For<IConfigService>();
        config.EnablePex.Returns(true);
        config.ExtensionUtPex.Returns(true);

        ProtocolExtensionPolicy.AllowPeerExchange(config, isPrivateTorrent: true).Should().BeFalse();
        ProtocolExtensionPolicy.AllowPeerExchange(config, isPrivateTorrent: false).Should().BeTrue();
    }

    [Test]
    public void GetPeerExchangeInterval_EnforcesMinimumTenSeconds()
    {
        var config = Substitute.For<IConfigService>();
        config.PexInterval.Returns(5);

        ProtocolExtensionPolicy.GetPeerExchangeInterval(config).Should().Be(TimeSpan.FromSeconds(10));

        config.PexInterval.Returns(45);
        ProtocolExtensionPolicy.GetPeerExchangeInterval(config).Should().Be(TimeSpan.FromSeconds(45));
    }

    [Test]
    public void GetPexMaxPeersPerMessage_EnforcesMinimumOne()
    {
        var config = Substitute.For<IConfigService>();
        config.PexMaxPeersPerMessage.Returns(0);

        ProtocolExtensionPolicy.GetPexMaxPeersPerMessage(config).Should().Be(1);
    }
}
