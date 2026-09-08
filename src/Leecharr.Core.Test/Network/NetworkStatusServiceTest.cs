// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.PortMapping;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NetworkStatusServiceTest
{
    private IExternalIpService externalIpService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private INatPmpPortMapperService natPmpPortMapperService = null!;

    [SetUp]
    public void SetUp()
    {
        this.externalIpService = Substitute.For<IExternalIpService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.natPmpPortMapperService = Substitute.For<INatPmpPortMapperService>();

        this.configFileProvider.Port.Returns(7889);
        this.configService.ListeningPort.Returns(51413);
        this.configService.BindInterface.Returns("Auto");
        this.externalIpService.CachedIp.Returns("203.0.113.10");
    }

    [Test]
    public void GetStatus_WithoutActiveNatPmpMappings_ReturnsFallbackPortMappings()
    {
        this.natPmpPortMapperService.ActiveMappings.Returns(new List<ActivePortMapping>());

        var service = new NetworkStatusService(
            this.externalIpService,
            this.configFileProvider,
            this.configService,
            this.natPmpPortMapperService);

        var status = service.GetStatus();

        status.Should().NotBeNull();
        status.PortMappings.Should().HaveCount(2);
        status.PortMappings[0].InternalPort.Should().Be(7889);
        status.PortMappings[0].ExternalPort.Should().Be(7889);
        status.PortMappings[0].Protocol.Should().Be("TCP");

        status.PortMappings[1].InternalPort.Should().Be(51413);
        status.PortMappings[1].ExternalPort.Should().Be(51413);
        status.PortMappings[1].Protocol.Should().Be("TCP/UDP");
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
            this.natPmpPortMapperService);

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
}
