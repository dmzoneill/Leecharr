// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.PortMapping;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NetworkStatusServiceTest
{
    private IExternalIpService externalIpService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private INatPmpPortMapperService natPmpPortMapperService = null!;
    private ISafeHttpClientService safeHttpClientService = null!;

    [SetUp]
    public void SetUp()
    {
        this.externalIpService = Substitute.For<IExternalIpService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.natPmpPortMapperService = Substitute.For<INatPmpPortMapperService>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.configFileProvider.Port.Returns(7889);
        this.configService.ListeningPort.Returns(51413);
        this.configService.BindInterface.Returns("Auto");
        this.configService.UpnpEnabled.Returns(true);
        this.externalIpService.CachedIp.Returns("203.0.113.10");
        this.externalIpService.GetExternalIpAsync().Returns(Task.FromResult("203.0.113.10"));
        this.safeHttpClientService
            .DownloadStringDirectAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("0"));
    }

    [Test]
    public void GetStatus_UsesGetExternalIpAsyncInsteadOfStaleCachedIp()
    {
        this.externalIpService.CachedIp.Returns("203.0.113.10");
        this.externalIpService.GetExternalIpAsync().Returns(Task.FromResult("203.0.113.55"));
        this.natPmpPortMapperService.ActiveMappings.Returns(new List<ActivePortMapping>());

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.ExternalIp.Should().Be("203.0.113.55");
        this.externalIpService.Received(1).GetExternalIpAsync();
    }

    [Test]
    public void GetStatus_WithoutActiveNatPmpMappings_ReturnsNoFabricatedPortMappings()
    {
        this.natPmpPortMapperService.ActiveMappings.Returns(new List<ActivePortMapping>());

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.Should().NotBeNull();
        status.PortMappings.Should().BeEmpty();
        status.PortOpen.Should().BeFalse();
        status.UpnpAvailable.Should().Be(NatPmpPortMapperService.DiscoverDefaultGateway() != null);
    }

    [Test]
    public void GetStatus_WhenPortCheckReturnsOpen_SetsPortOpenTrue()
    {
        this.natPmpPortMapperService.ActiveMappings.Returns(new List<ActivePortMapping>());
        this.safeHttpClientService
            .DownloadStringDirectAsync("https://portcheck.transmissionbt.com/51413", Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("1"));

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.PortOpen.Should().BeTrue();
    }

    [Test]
    public void GetStatus_WhenUpnpEnabledInConfig_DoesNotForceUpnpAvailableTrue()
    {
        this.configService.UpnpEnabled.Returns(true);
        this.natPmpPortMapperService.ActiveMappings.Returns(new List<ActivePortMapping>());

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        if (NatPmpPortMapperService.DiscoverDefaultGateway() == null)
        {
            status.UpnpAvailable.Should().BeFalse();
        }
    }

    [Test]
    public void GetStatus_WithActiveNatPmpMappings_ReturnsDynamicPortMappings()
    {
        var activeMappings = new List<ActivePortMapping>
        {
            new()
            {
                InternalPort = 51413,
                ExternalPort = 52000,
                Protocol = NatPmpProtocol.Tcp,
                GatewayAddress = IPAddress.Loopback,
                LifetimeSeconds = 3600,
            },
            new()
            {
                InternalPort = 51413,
                ExternalPort = 52000,
                Protocol = NatPmpProtocol.Udp,
                GatewayAddress = IPAddress.Loopback,
                LifetimeSeconds = 3600,
            },
        };

        this.natPmpPortMapperService.ActiveMappings.Returns(activeMappings);

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.Should().NotBeNull();
        status.PortMappings.Should().HaveCount(2);

        status.PortMappings[0].InternalPort.Should().Be(51413);
        status.PortMappings[0].ExternalPort.Should().Be(52000);
        status.PortMappings[0].Protocol.Should().Be("TCP");
        status.PortMappings[0].Description.Should().Be("BitTorrent Peer Swarm & DHT");
        status.PortMappings[0].IsActive.Should().BeTrue();

        status.PortMappings[1].InternalPort.Should().Be(51413);
        status.PortMappings[1].ExternalPort.Should().Be(52000);
        status.PortMappings[1].Protocol.Should().Be("UDP");
        status.PortMappings[1].Description.Should().Be("BitTorrent Peer Swarm & DHT");
        status.PortMappings[1].IsActive.Should().BeTrue();
    }

    [Test]
    public void GetStatus_WhenVpnKillSwitchIsActive_ReturnsVpnKillSwitchActiveTrue()
    {
        var vpnKillSwitchService = Substitute.For<NzbDrone.Core.Network.Vpn.IVpnKillSwitchService>();
        vpnKillSwitchService.IsFailClosedActive.Returns(true);

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            vpnKillSwitchService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.Should().NotBeNull();
        status.VpnKillSwitchActive.Should().BeTrue();
    }

    [Test]
    public void GetStatus_WhenVpnKillSwitchIsNotActive_ReturnsVpnKillSwitchActiveFalse()
    {
        var vpnKillSwitchService = Substitute.For<NzbDrone.Core.Network.Vpn.IVpnKillSwitchService>();
        vpnKillSwitchService.IsFailClosedActive.Returns(false);

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            vpnKillSwitchService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.Should().NotBeNull();
        status.VpnKillSwitchActive.Should().BeFalse();
    }

    [Test]
    public void GetStatus_WhenVpnKillSwitchServiceIsNull_ReturnsVpnKillSwitchActiveFalse()
    {
        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            vpnKillSwitchService: null,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.Should().NotBeNull();
        status.VpnKillSwitchActive.Should().BeFalse();
    }

    [Test]
    public void GetStatus_WhenBindInterfaceIsLiteralIp_UsesThatForLocalIp()
    {
        this.configService.BindInterface.Returns("203.0.113.50");
        this.natPmpPortMapperService.ActiveMappings.Returns(new List<ActivePortMapping>());

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();

        status.LocalIp.Should().Be("203.0.113.50");
        status.ActiveInterface.Should().Be("203.0.113.50");
    }

    [Test]
    public void GetLocalAddresses_IncludesUsableIpv6WhenPresent()
    {
        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var addresses = service.GetLocalAddresses();

        addresses.Should().OnlyContain(a =>
            IPAddress.TryParse(a, out var ip) &&
            (ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6));

        var hasGlobalIpv6 = NetworkInterface.GetAllNetworkInterfaces()
            .Any(ni => ni.OperationalStatus == OperationalStatus.Up &&
                       ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                       ni.GetIPProperties().UnicastAddresses.Any(u =>
                           u.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                           !u.Address.IsIPv6LinkLocal &&
                           !u.Address.IsIPv6SiteLocal &&
                           !u.Address.IsIPv6UniqueLocal));

        if (hasGlobalIpv6)
        {
            addresses.Should().Contain(a => IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6);
        }
    }

    [Test]
    public void GetStatus_WhenBoundToSpecificInterface_LocalIpMatchesInterfaceAddress()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                 n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 (ManagedSocketBindingProvider.GetInterfaceIp(n.Name, AddressFamily.InterNetwork) != null ||
                                  ManagedSocketBindingProvider.GetInterfaceIp(n.Name, AddressFamily.InterNetworkV6) != null));

        if (nic == null)
        {
            Assert.Ignore("No suitable network interface for bind test");
        }

        this.configService.BindInterface.Returns(nic.Name);
        this.natPmpPortMapperService.ActiveMappings.Returns(new List<ActivePortMapping>());

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService,
            safeHttpClientService: this.safeHttpClientService);

        var status = service.GetStatus();
        var expected = ManagedSocketBindingProvider.GetInterfaceIp(nic.Name, AddressFamily.InterNetwork)
            ?? ManagedSocketBindingProvider.GetInterfaceIp(nic.Name, AddressFamily.InterNetworkV6);

        expected.Should().NotBeNull();
        status.LocalIp.Should().Be(expected!.ToString());
        status.LocalAddresses.Should().Contain(status.LocalIp);
    }
}
