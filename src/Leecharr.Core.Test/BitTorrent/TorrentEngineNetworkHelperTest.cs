// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class TorrentEngineNetworkHelperTest
{
    [Test]
    public void ResolveBoundIpv4Address_WhenDirectIpv4Configured_ReturnsDirectIp()
    {
        var configService = Substitute.For<IConfigService>();
        configService.NetworkInterfaceBinding.Returns("192.168.1.150");

        var ip = TorrentEngineNetworkHelper.ResolveBoundIpv4Address(
            false,
            Substitute.For<IVpnKillSwitchService>(),
            configService,
            Substitute.For<INetworkBindingService>());

        ip.Should().Be("192.168.1.150");
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenDirectIpv6Configured_ReturnsDirectIp()
    {
        var configService = Substitute.For<IConfigService>();
        configService.NetworkInterfaceBinding.Returns("2001:db8::10");

        var ip = TorrentEngineNetworkHelper.ResolveBoundIpv4Address(
            false,
            Substitute.For<IVpnKillSwitchService>(),
            configService,
            Substitute.For<INetworkBindingService>());

        ip.Should().Be("2001:db8::10");
        ip.Should().NotBe("127.0.0.1");
    }
}
