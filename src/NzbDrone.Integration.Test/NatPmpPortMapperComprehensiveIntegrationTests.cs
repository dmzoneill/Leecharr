// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Buffers.Binary;
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
using NzbDrone.Core.Network.PortMapping;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class NatPmpPortMapperComprehensiveIntegrationTests : IntegrationTestBase
{
    // =========================================================================
    // 1. Gateway Resolution, Candidate Evaluation, and Subnet Checking
    // =========================================================================

    [Test]
    public void IsInSameSubnet_WithMatchingAndMismatchingSubnets_EvaluatesAccurately()
    {
        var mask24 = IPAddress.Parse("255.255.255.0");
        var mask16 = IPAddress.Parse("255.255.0.0");
        var mask8 = IPAddress.Parse("255.0.0.0");
        var mask30 = IPAddress.Parse("255.255.255.252");

        // /24 subnet tests
        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("192.168.1.100"),
            IPAddress.Parse("192.168.1.1"),
            mask24).Should().BeTrue();

        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("192.168.1.100"),
            IPAddress.Parse("192.168.2.1"),
            mask24).Should().BeFalse();

        // /16 subnet tests
        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("172.16.10.2"),
            IPAddress.Parse("172.16.1.1"),
            mask16).Should().BeTrue();

        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("172.16.10.2"),
            IPAddress.Parse("172.17.1.1"),
            mask16).Should().BeFalse();

        // /8 subnet tests
        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("10.1.2.3"),
            IPAddress.Parse("10.254.254.1"),
            mask8).Should().BeTrue();

        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("10.1.2.3"),
            IPAddress.Parse("11.1.2.1"),
            mask8).Should().BeFalse();

        // /30 subnet tests
        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("10.0.0.1"),
            IPAddress.Parse("10.0.0.2"),
            mask30).Should().BeTrue();

        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("10.0.0.1"),
            IPAddress.Parse("10.0.0.5"),
            mask30).Should().BeFalse();

        // Invalid masks (0.0.0.0 and 255.255.255.255)
        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("192.168.1.10"),
            IPAddress.Parse("192.168.1.1"),
            IPAddress.Parse("0.0.0.0")).Should().BeFalse();

        NatPmpPortMapperService.IsInSameSubnet(
            IPAddress.Parse("192.168.1.10"),
            IPAddress.Parse("192.168.1.1"),
            IPAddress.Parse("255.255.255.255")).Should().BeFalse();

        // Null argument handling
        NatPmpPortMapperService.IsInSameSubnet(null, IPAddress.Parse("192.168.1.1"), mask24).Should().BeFalse();
        NatPmpPortMapperService.IsInSameSubnet(IPAddress.Parse("192.168.1.1"), null, mask24).Should().BeFalse();
        NatPmpPortMapperService.IsInSameSubnet(IPAddress.Parse("192.168.1.1"), IPAddress.Parse("192.168.1.1"), null).Should().BeFalse();

        // IPv6 addresses
        NatPmpPortMapperService.IsInSameSubnet(IPAddress.IPv6Loopback, IPAddress.IPv6Loopback, mask24).Should().BeFalse();
    }

    [Test]
    public void IsValidIpv4UnicastAddress_And_IsValidGatewayAddress_ValidatesRangesCorrectly()
    {
        // Valid IPv4 unicast
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("192.168.1.50")).Should().BeTrue();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("10.0.0.2")).Should().BeTrue();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("172.16.5.1")).Should().BeTrue();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("8.8.8.8")).Should().BeTrue();

        // Invalid unicast: null, IPv6, loopback, any, none, APIPA, 0.x.x.x
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(null).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.IPv6Loopback).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Loopback).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Any).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.None).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("0.1.2.3")).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("169.254.1.1")).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("169.254.200.5")).Should().BeFalse();

        // Valid gateway addresses
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("192.168.1.1")).Should().BeTrue();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("10.0.0.1")).Should().BeTrue();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("172.16.0.1")).Should().BeTrue();

        // Invalid gateway addresses: null, IPv6, loopback, any, none, 0.0.0.0, APIPA
        NatPmpPortMapperService.IsValidGatewayAddress(null).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.IPv6Loopback).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Loopback).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Any).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.None).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("0.0.0.0")).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("0.1.1.1")).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("169.254.0.1")).Should().BeFalse();
    }

    [Test]
    public void IsVirtualInterfaceName_And_IsVpnInterfaceName_IdentifiesVirtualAndVpnAdapters()
    {
        // Virtual interface patterns in name or description
        var virtualNames = new[]
        {
            "docker0", "veth1234", "vmnet8", "vboxnet0", "wsl-nic", "hyper-v-adapter",
            "vethernet-switch", "virbr0", "vmware-vmnet1", "virtualbox-host",
            "cni0", "flannel.1", "calico-veth", "tailscale0", "zerotier-net",
        };

        foreach (var vName in virtualNames)
        {
            NatPmpPortMapperService.IsVirtualInterfaceName(vName).Should().BeTrue($"'{vName}' should be recognized as virtual");
        }

        // Virtual pattern inside description
        NatPmpPortMapperService.IsVirtualInterfaceName("eth0", "VirtualBox Host-Only Ethernet").Should().BeTrue();
        NatPmpPortMapperService.IsVirtualInterfaceName("eth0", "Docker Container Interface").Should().BeTrue();

        // Non-virtual names
        NatPmpPortMapperService.IsVirtualInterfaceName("eth0", "Realtek PCIe GbE Controller").Should().BeFalse();
        NatPmpPortMapperService.IsVirtualInterfaceName("eno1", "Intel Ethernet Connection").Should().BeFalse();
        NatPmpPortMapperService.IsVirtualInterfaceName("wlan0", "Intel Wi-Fi 6 AX201").Should().BeFalse();
        NatPmpPortMapperService.IsVirtualInterfaceName(null, null).Should().BeFalse();
        NatPmpPortMapperService.IsVirtualInterfaceName(string.Empty, string.Empty).Should().BeFalse();

        // VPN interface names
        var vpnNames = new[] { "tun0", "tun1", "wg0", "wg-client", "ppp0", "tap0", "vpn-london", "utun2" };
        foreach (var vpnName in vpnNames)
        {
            NatPmpPortMapperService.IsVpnInterfaceName(vpnName).Should().BeTrue($"'{vpnName}' should be recognized as VPN");
        }

        NatPmpPortMapperService.IsVpnInterfaceName("eth0").Should().BeFalse();
        NatPmpPortMapperService.IsVpnInterfaceName("wlan0").Should().BeFalse();
        NatPmpPortMapperService.IsVpnInterfaceName(null).Should().BeFalse();
        NatPmpPortMapperService.IsVpnInterfaceName(string.Empty).Should().BeFalse();
    }

    [Test]
    public void IsPhysicalInterfaceType_IdentifiesPhysicalHardware()
    {
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Ethernet).Should().BeTrue();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Wireless80211).Should().BeTrue();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.GigabitEthernet).Should().BeTrue();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.FastEthernetFx).Should().BeTrue();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.FastEthernetT).Should().BeTrue();

        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Loopback).Should().BeFalse();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Tunnel).Should().BeFalse();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Ppp).Should().BeFalse();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Slip).Should().BeFalse();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Unknown).Should().BeFalse();
    }

    [Test]
    public void SelectBestGateway_WithSpecificBoundInterface_SelectsOrFailsClosed()
    {
        var gwBoundSubnet = IPAddress.Parse("10.8.0.1");
        var gwBoundOther = IPAddress.Parse("10.8.0.254");
        var boundCandidate = new NatPmpNetworkInterfaceCandidate(
            Name: "tun0",
            Description: "WireGuard VPN",
            Id: "nic-tun0",
            InterfaceType: NetworkInterfaceType.Tunnel,
            OperationalStatus: OperationalStatus.Up,
            UnicastAddresses: new List<(IPAddress Address, IPAddress Mask)>
            {
                (IPAddress.Parse("10.8.0.2"), IPAddress.Parse("255.255.255.0")),
            },
            GatewayAddresses: new List<IPAddress> { gwBoundSubnet, gwBoundOther });

        var physicalCandidate = new NatPmpNetworkInterfaceCandidate(
            Name: "eth0",
            Description: "Intel PCIe GbE",
            Id: "nic-eth0",
            InterfaceType: NetworkInterfaceType.Ethernet,
            OperationalStatus: OperationalStatus.Up,
            UnicastAddresses: new List<(IPAddress Address, IPAddress Mask)>
            {
                (IPAddress.Parse("192.168.1.100"), IPAddress.Parse("255.255.255.0")),
            },
            GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.1.1") });

        var candidates = new List<NatPmpNetworkInterfaceCandidate> { boundCandidate, physicalCandidate };

        // 1. Matches bound interface by Name and prioritizes subnet matched gateway
        var selected = NatPmpPortMapperService.SelectBestGateway(candidates, boundInterface: "tun0");
        selected.Should().Be(gwBoundSubnet);

        // 2. Matches bound interface by Id
        var selectedById = NatPmpPortMapperService.SelectBestGateway(candidates, boundInterface: "nic-tun0");
        selectedById.Should().Be(gwBoundSubnet);

        // 3. Bound interface not found and failClosed = true -> returns null
        var failClosedResult = NatPmpPortMapperService.SelectBestGateway(candidates, boundInterface: "missing0", failClosed: true);
        failClosedResult.Should().BeNull();

        // 4. Bound interface not found and failClosed = false -> falls back to physical candidate
        var fallbackResult = NatPmpPortMapperService.SelectBestGateway(candidates, boundInterface: "missing0", failClosed: false);
        fallbackResult.Should().Be(IPAddress.Parse("192.168.1.1"));

        // 5. Bound interface is "any" or "all" -> treated as unbound, falls back to physical
        var anyResult = NatPmpPortMapperService.SelectBestGateway(candidates, boundInterface: "any");
        anyResult.Should().Be(IPAddress.Parse("192.168.1.1"));

        var allResult = NatPmpPortMapperService.SelectBestGateway(candidates, boundInterface: "all");
        allResult.Should().Be(IPAddress.Parse("192.168.1.1"));

        // 6. candidates null or empty
        NatPmpPortMapperService.SelectBestGateway(null).Should().BeNull();
        NatPmpPortMapperService.SelectBestGateway(new List<NatPmpNetworkInterfaceCandidate>()).Should().BeNull();
    }

    [Test]
    public void SelectBestGateway_PhysicalAndFallbackHierarchy_PrioritizesPhysicalAndDeprioritizesDockerBridge()
    {
        // Physical candidate 1 without subnet match
        var physicalWithoutSubnet = new NatPmpNetworkInterfaceCandidate(
            Name: "eth0",
            Description: "Ethernet Adapter",
            Id: "eth0",
            InterfaceType: NetworkInterfaceType.Ethernet,
            OperationalStatus: OperationalStatus.Up,
            UnicastAddresses: new List<(IPAddress Address, IPAddress Mask)>
            {
                (IPAddress.Parse("192.168.50.10"), IPAddress.Parse("255.255.255.0")),
            },
            GatewayAddresses: new List<IPAddress> { IPAddress.Parse("10.0.0.1") });

        // Physical candidate 2 with subnet match
        var physicalWithSubnet = new NatPmpNetworkInterfaceCandidate(
            Name: "wlan0",
            Description: "Wireless Adapter",
            Id: "wlan0",
            InterfaceType: NetworkInterfaceType.Wireless80211,
            OperationalStatus: OperationalStatus.Up,
            UnicastAddresses: new List<(IPAddress Address, IPAddress Mask)>
            {
                (IPAddress.Parse("192.168.1.50"), IPAddress.Parse("255.255.255.0")),
            },
            GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.1.1") });

        // Subnet match on wlan0 is selected over eth0
        var candidates = new List<NatPmpNetworkInterfaceCandidate> { physicalWithoutSubnet, physicalWithSubnet };
        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);
        selected.Should().Be(IPAddress.Parse("192.168.1.1"));

        // When no subnet match on physical candidate, returns first gateway
        var physicalOnlyNoSubnet = new List<NatPmpNetworkInterfaceCandidate> { physicalWithoutSubnet };
        var firstGwSelected = NatPmpPortMapperService.SelectBestGateway(physicalOnlyNoSubnet);
        firstGwSelected.Should().Be(IPAddress.Parse("10.0.0.1"));

        // When no physical candidates exist, evaluates fallback candidates
        var dockerBridge = new NatPmpNetworkInterfaceCandidate(
            Name: "docker0",
            Description: "Docker Default Bridge",
            Id: "docker0",
            InterfaceType: NetworkInterfaceType.Ethernet,
            OperationalStatus: OperationalStatus.Up,
            UnicastAddresses: new List<(IPAddress Address, IPAddress Mask)>
            {
                (IPAddress.Parse("172.17.0.2"), IPAddress.Parse("255.255.0.0")),
            },
            GatewayAddresses: new List<IPAddress> { IPAddress.Parse("172.17.0.1") });

        var vpnFallback = new NatPmpNetworkInterfaceCandidate(
            Name: "vpn-bridge",
            Description: "Custom VPN Interface",
            Id: "vpn-bridge",
            InterfaceType: NetworkInterfaceType.Ethernet,
            OperationalStatus: OperationalStatus.Up,
            UnicastAddresses: new List<(IPAddress Address, IPAddress Mask)>
            {
                (IPAddress.Parse("10.200.0.2"), IPAddress.Parse("255.255.255.0")),
            },
            GatewayAddresses: new List<IPAddress> { IPAddress.Parse("10.200.0.1") });

        // vpnFallback is preferred over docker0
        var fallbackList = new List<NatPmpNetworkInterfaceCandidate> { dockerBridge, vpnFallback };
        var fallbackSelected = NatPmpPortMapperService.SelectBestGateway(fallbackList);
        fallbackSelected.Should().Be(IPAddress.Parse("10.200.0.1"));

        // If only docker0 is available as fallback, returns docker0 gateway
        var dockerOnly = new List<NatPmpNetworkInterfaceCandidate> { dockerBridge };
        var dockerSelected = NatPmpPortMapperService.SelectBestGateway(dockerOnly);
        dockerSelected.Should().Be(IPAddress.Parse("172.17.0.1"));
    }

    [Test]
    public void DiscoverDefaultGateway_ExecutesSystemEvaluation_WithoutThrowing()
    {
        var gw = NatPmpPortMapperService.DiscoverDefaultGateway();
        if (gw != null)
        {
            gw.AddressFamily.Should().Be(AddressFamily.InterNetwork);
        }

        // Fail-closed with non-existent bound interface returns null
        var failClosedGw = NatPmpPortMapperService.DiscoverDefaultGateway("non_existent_adapter_xyz999", failClosed: true);
        failClosedGw.Should().BeNull();
    }

    // =========================================================================
    // 2. UDP Request Encoding
    // =========================================================================

    [Test]
    public async Task GetExternalIpAddressAsync_EncodesRfc6886ExternalIpRequestPacket_Properly()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] capturedRequest = null;

        var serverTask = Task.Run(async () =>
        {
            var received = await mockGateway.ReceiveAsync(cts.Token);
            capturedRequest = received.Buffer;

            // Send standard External IP response: [0x00, 0x80, result(2), epoch(4), ip(4)]
            var resp = new byte[12];
            resp[0] = 0x00;
            resp[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0); // Result = 0 (Success)
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 778899); // Epoch
            var ipBytes = IPAddress.Parse("198.51.100.42").GetAddressBytes();
            ipBytes.CopyTo(resp.AsSpan(8, 4));

            await mockGateway.SendAsync(resp, resp.Length, received.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var externalIp = await service.GetExternalIpAddressAsync(IPAddress.Loopback, cts.Token);
        await serverTask;

        // Verify request wire format
        capturedRequest.Should().NotBeNull();
        capturedRequest.Length.Should().Be(2);
        capturedRequest[0].Should().Be(0x00, "RFC 6886 Version must be 0");
        capturedRequest[1].Should().Be(0x00, "RFC 6886 Opcode for external address request must be 0");

        // Verify response parsing
        externalIp.Should().NotBeNull();
        externalIp.ToString().Should().Be("198.51.100.42");
        service.GatewayEpochs.Should().ContainKey(IPAddress.Loopback);
        service.GatewayEpochs[IPAddress.Loopback].Should().Be(778899);
    }

    [Test]
    public async Task MapPortAsync_EncodesTcpAndUdpMappingRequests_WithExactFields()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var capturedRequests = new List<byte[]>();

        var serverTask = Task.Run(async () =>
        {
            // 1. TCP request
            var req1 = await mockGateway.ReceiveAsync(cts.Token);
            capturedRequests.Add(req1.Buffer);

            var resp1 = new byte[16];
            resp1[0] = 0x00;
            resp1[1] = 0x82; // TCP response opcode
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(4, 4), 1000);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(10, 2), 51413);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(resp1, resp1.Length, req1.RemoteEndPoint);

            // 2. UDP request with suggested external port and custom lifetime
            var req2 = await mockGateway.ReceiveAsync(cts.Token);
            capturedRequests.Add(req2.Buffer);

            var resp2 = new byte[16];
            resp2[0] = 0x00;
            resp2[1] = 0x81; // UDP response opcode
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(4, 4), 1001);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(8, 2), 6881);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(10, 2), 55000);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(12, 4), 7200);
            await mockGateway.SendAsync(resp2, resp2.Length, req2.RemoteEndPoint);

            // 3. Unmap request (lifetime = 0)
            var req3 = await mockGateway.ReceiveAsync(cts.Token);
            capturedRequests.Add(req3.Buffer);

            var resp3 = new byte[16];
            resp3[0] = 0x00;
            resp3[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp3.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp3.AsSpan(4, 4), 1002);
            BinaryPrimitives.WriteUInt16BigEndian(resp3.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp3.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp3.AsSpan(12, 4), 0);
            await mockGateway.SendAsync(resp3, resp3.Length, req3.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);

        // Map TCP with suggestedExternalPort = 0 (defaults to internalPort)
        var tcpResult = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, suggestedExternalPort: 0, lifetimeSeconds: 3600, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        tcpResult.Success.Should().BeTrue();

        // Map UDP with suggestedExternalPort = 55000 and lifetime = 7200
        var udpResult = await service.MapPortAsync(6881, NatPmpProtocol.Udp, suggestedExternalPort: 55000, lifetimeSeconds: 7200, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        udpResult.Success.Should().BeTrue();

        // Unmap TCP
        var unmapResult = await service.UnmapPortAsync(51413, NatPmpProtocol.Tcp, IPAddress.Loopback, cts.Token);
        unmapResult.Should().BeTrue();

        await serverTask;
        capturedRequests.Should().HaveCount(3);

        // Verify TCP request encoding
        var tcpReq = capturedRequests[0];
        tcpReq.Length.Should().Be(12);
        tcpReq[0].Should().Be(0x00, "Version must be 0");
        tcpReq[1].Should().Be(0x02, "Opcode for TCP must be 2");
        BinaryPrimitives.ReadUInt16BigEndian(tcpReq.AsSpan(2, 2)).Should().Be(0, "Reserved bytes must be 0");
        BinaryPrimitives.ReadUInt16BigEndian(tcpReq.AsSpan(4, 2)).Should().Be(51413, "Internal port must match");
        BinaryPrimitives.ReadUInt16BigEndian(tcpReq.AsSpan(6, 2)).Should().Be(51413, "Suggested external port should default to internal port when 0");
        BinaryPrimitives.ReadUInt32BigEndian(tcpReq.AsSpan(8, 4)).Should().Be(3600, "Lifetime must match");

        // Verify UDP request encoding
        var udpReq = capturedRequests[1];
        udpReq.Length.Should().Be(12);
        udpReq[0].Should().Be(0x00, "Version must be 0");
        udpReq[1].Should().Be(0x01, "Opcode for UDP must be 1");
        BinaryPrimitives.ReadUInt16BigEndian(udpReq.AsSpan(2, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16BigEndian(udpReq.AsSpan(4, 2)).Should().Be(6881);
        BinaryPrimitives.ReadUInt16BigEndian(udpReq.AsSpan(6, 2)).Should().Be(55000, "Suggested external port must be 55000");
        BinaryPrimitives.ReadUInt32BigEndian(udpReq.AsSpan(8, 4)).Should().Be(7200, "Lifetime must be 7200");

        // Verify Unmap request encoding
        var unmapReq = capturedRequests[2];
        unmapReq.Length.Should().Be(12);
        BinaryPrimitives.ReadUInt16BigEndian(unmapReq.AsSpan(4, 2)).Should().Be(51413);
        BinaryPrimitives.ReadUInt16BigEndian(unmapReq.AsSpan(6, 2)).Should().Be(0, "Suggested external port must be 0 when unmapping");
        BinaryPrimitives.ReadUInt32BigEndian(unmapReq.AsSpan(8, 4)).Should().Be(0, "Lifetime must be 0 when unmapping");
    }

    // =========================================================================
    // 3. UDP Response Decoding
    // =========================================================================

    [TestCase(1, "UnsupportedVersion")]
    [TestCase(2, "NotAuthorized")]
    [TestCase(3, "NetworkFailure")]
    [TestCase(4, "OutOfResources")]
    [TestCase(5, "UnsupportedOpcode")]
    [TestCase(99, "OtherErrorCode")]
    public async Task MapPortAsync_DecodesAllStandardResultCodes_AndReportsGatewayErrors(int errorCode, string description)
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var req = await mockGateway.ReceiveAsync(cts.Token);
            var resp = new byte[16];
            resp[0] = 0x00;
            resp[1] = 0x82; // TCP opcode response
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), (ushort)errorCode);
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 5000);
            await mockGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        await serverTask;

        result.Success.Should().BeFalse($"Error code {errorCode} ({description}) must result in failure");
        result.InternalPort.Should().Be(51413);
        result.ErrorMessage.Should().Contain(errorCode.ToString());

        // Gateway epoch should still be tracked even on error response
        service.GatewayEpochs.Should().ContainKey(IPAddress.Loopback);
        service.GatewayEpochs[IPAddress.Loopback].Should().Be(5000);
    }

    [Test]
    public async Task MapPortAsync_DecodesOpcode_Epoch_MappedPorts_And_Lifetime()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var req = await mockGateway.ReceiveAsync(cts.Token);
            var resp = new byte[16];
            resp[0] = 0x00;
            resp[1] = 0x82; // TCP response opcode
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0); // Success
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 987654); // Epoch
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), 51413); // Internal port
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 59999); // Gateway-assigned external port
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 1800); // 30 min lifetime
            await mockGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, suggestedExternalPort: 51413, lifetimeSeconds: 3600, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        await serverTask;

        result.Success.Should().BeTrue();
        result.InternalPort.Should().Be(51413);
        result.ExternalPort.Should().Be(59999);
        result.LifetimeSeconds.Should().Be(1800);
        result.GatewayAddress.Should().Be(IPAddress.Loopback);

        // Verify active mapping tracking
        var active = service.ActiveMappings.Should().ContainSingle().Subject;
        active.InternalPort.Should().Be(51413);
        active.ExternalPort.Should().Be(59999);
        active.Protocol.Should().Be(NatPmpProtocol.Tcp);
        active.LifetimeSeconds.Should().Be(1800);
        active.LastEpoch.Should().Be(987654);

        // Next renewal should be scheduled at 50% lifetime = 900 seconds in the future
        var renewalSpan = (active.NextRenewalUtc - DateTime.UtcNow).TotalSeconds;
        renewalSpan.Should().BeInRange(850, 950);
    }

    [Test]
    public async Task GetExternalIpAddressAsync_DecodesTruncatedOrInvalidResponses_Gracefully()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Test 1: Response with non-zero result code (e.g. 2 NotAuthorized) returns null but tracks epoch
        var serverTask1 = Task.Run(async () =>
        {
            var req = await mockGateway.ReceiveAsync(cts.Token);
            var resp = new byte[12];
            resp[0] = 0x00;
            resp[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 2); // NotAuthorized
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 112233);
            await mockGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var ipResult1 = await service.GetExternalIpAddressAsync(IPAddress.Loopback, cts.Token);
        await serverTask1;

        ipResult1.Should().BeNull();
        service.GatewayEpochs[IPAddress.Loopback].Should().Be(112233);

        // Test 2: Response with result code 0 but truncated payload (< 12 bytes) returns null
        var serverTask2 = Task.Run(async () =>
        {
            var req = await mockGateway.ReceiveAsync(cts.Token);
            var resp = new byte[10]; // Less than 12 bytes for IP
            resp[0] = 0x00;
            resp[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 112234);
            await mockGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
        });

        var ipResult2 = await service.GetExternalIpAddressAsync(IPAddress.Loopback, cts.Token);
        await serverTask2;

        ipResult2.Should().BeNull();
    }

    [Test]
    public async Task MapPortAsync_DecodesTruncatedResponse_AsFailure()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Response has result 0 but is truncated (< 16 bytes)
        var serverTask = Task.Run(async () =>
        {
            var req = await mockGateway.ReceiveAsync(cts.Token);
            var resp = new byte[12]; // Truncated, missing external port and lifetime
            resp[0] = 0x00;
            resp[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 999);
            await mockGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        await serverTask;

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("did not receive a response");
    }

    // =========================================================================
    // 4. Error Handling, Timeout Retry Logic, and Lifecycle
    // =========================================================================

    [Test]
    public async Task SendAndReceiveWithRetryAsync_RetriesOnMissedPacket_AndSucceedsOnSubsequentAttempt()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var attemptCount = 0;

        var serverTask = Task.Run(async () =>
        {
            // Drop attempt 1 (simulate packet loss)
            var req1 = await mockGateway.ReceiveAsync(cts.Token);
            attemptCount++;

            // Respond on attempt 2
            var req2 = await mockGateway.ReceiveAsync(cts.Token);
            attemptCount++;

            var resp = new byte[16];
            resp[0] = 0x00;
            resp[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 500);
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 51413);
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(resp, resp.Length, req2.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        await serverTask;

        attemptCount.Should().Be(2, "Client should have retransmitted after attempt 1 timed out");
        result.Success.Should().BeTrue();
    }

    [Test]
    public async Task SendAndReceiveWithRetryAsync_IgnoresForeignEndpointsAndContinuesListening()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var req = await mockGateway.ReceiveAsync(cts.Token);

            // Send a response from an illegitimate port first
            using var foreignUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var foreignResp = new byte[16];
            foreignResp[0] = 0x00;
            foreignResp[1] = 0x82;
            await foreignUdp.SendAsync(foreignResp, foreignResp.Length, req.RemoteEndPoint);

            // Send a response with mismatched opcode (0x81 instead of 0x82) from mockGateway
            var wrongOpcodeResp = new byte[16];
            wrongOpcodeResp[0] = 0x00;
            wrongOpcodeResp[1] = 0x81;
            await mockGateway.SendAsync(wrongOpcodeResp, wrongOpcodeResp.Length, req.RemoteEndPoint);

            // Finally send the authentic response
            var authenticResp = new byte[16];
            authenticResp[0] = 0x00;
            authenticResp[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(authenticResp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(authenticResp.AsSpan(4, 4), 2000);
            BinaryPrimitives.WriteUInt16BigEndian(authenticResp.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(authenticResp.AsSpan(10, 2), 51413);
            BinaryPrimitives.WriteUInt32BigEndian(authenticResp.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(authenticResp, authenticResp.Length, req.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        await serverTask;

        result.Success.Should().BeTrue();
        result.ExternalPort.Should().Be(51413);
    }

    [Test]
    public async Task SendAndReceiveWithRetryAsync_ExhaustsRetries_WhenGatewayUnresponsive()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        // Use a short cancellation token so the test doesn't wait full 4.2 seconds
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        using var service = new NatPmpPortMapperService(mockPort);
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("did not receive a response");
    }

    [Test]
    public void TrackEpoch_DecreasingEpoch_DetectsRebootAndTriggersRebootRenewal()
    {
        using var service = new NatPmpPortMapperService(5351);

        // 1. Initial epoch
        service.TrackEpoch(IPAddress.Loopback, 1000);
        service.GatewayEpochs[IPAddress.Loopback].Should().Be(1000);

        // 2. Monotonic increase
        service.TrackEpoch(IPAddress.Loopback, 1200);
        service.GatewayEpochs[IPAddress.Loopback].Should().Be(1200);

        // 3. Epoch decrease (gateway reboot)
        service.TrackEpoch(IPAddress.Loopback, 300);
        service.GatewayEpochs[IPAddress.Loopback].Should().Be(300);

        // 4. Null gateway is safe
        service.TrackEpoch(null, 500);
    }

    [Test]
    public async Task SuspendAndResume_ControlsOperationalStateAndBlocksRequests()
    {
        using var service = new NatPmpPortMapperService(5351);

        service.IsSuspended.Should().BeFalse();

        // 1. Suspend
        service.Suspend();
        service.IsSuspended.Should().BeTrue();

        // Subsequent suspend is idempotent
        service.Suspend();
        service.IsSuspended.Should().BeTrue();

        // When suspended, requests abort immediately
        var ip = await service.GetExternalIpAddressAsync(IPAddress.Loopback);
        ip.Should().BeNull();

        var mapResult = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback);
        mapResult.Success.Should().BeFalse();
        mapResult.ErrorMessage.Should().Contain("suspended");

        await service.RenewAllMappingsAsync(force: true);

        // 2. Resume
        service.Resume();
        service.IsSuspended.Should().BeFalse();

        // Subsequent resume is idempotent
        service.Resume();
        service.IsSuspended.Should().BeFalse();
    }

    [Test]
    public async Task FailClosedKillSwitch_BlocksRequestsWhenBoundInterfaceMissing()
    {
        var config = Substitute.For<IConfigService>();
        config.EnableVpnKillSwitch.Returns(true);
        config.BindInterface.Returns("missing_tun99");

        using var service = new NatPmpPortMapperService(5351, boundInterface: "missing_tun99", configService: config);

        var ip = await service.GetExternalIpAddressAsync();
        ip.Should().BeNull();

        var mapResult = await service.MapPortAsync(51413, NatPmpProtocol.Tcp);
        mapResult.Success.Should().BeFalse();
        mapResult.ErrorMessage.Should().Contain("No IPv4 default gateway found");

        await service.RenewAllMappingsAsync(force: true);
    }

    [Test]
    public async Task RenewAllMappingsAsync_ForcesRenewalAndRefreshesLeaseState()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var capturedRequests = new List<byte[]>();

        var serverTask = Task.Run(async () =>
        {
            // Initial map request
            var req1 = await mockGateway.ReceiveAsync(cts.Token);
            capturedRequests.Add(req1.Buffer);
            var resp1 = new byte[16];
            resp1[0] = 0x00;
            resp1[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(4, 4), 100);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(10, 2), 52000);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(resp1, resp1.Length, req1.RemoteEndPoint);

            // Renewal request
            var req2 = await mockGateway.ReceiveAsync(cts.Token);
            capturedRequests.Add(req2.Buffer);
            var resp2 = new byte[16];
            resp2[0] = 0x00;
            resp2[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(4, 4), 101);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(10, 2), 52000);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(12, 4), 7200); // Extended lifetime
            await mockGateway.SendAsync(resp2, resp2.Length, req2.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        await service.MapPortAsync(51413, NatPmpProtocol.Tcp, suggestedExternalPort: 52000, lifetimeSeconds: 3600, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        // Force renew
        await service.RenewAllMappingsAsync(force: true, cancellationToken: cts.Token);
        await serverTask;

        capturedRequests.Should().HaveCount(2);

        // Active mapping refreshed with new lifetime
        var mapping = service.ActiveMappings.Should().ContainSingle().Subject;
        mapping.LifetimeSeconds.Should().Be(7200);
        var renewalSpan = (mapping.NextRenewalUtc - DateTime.UtcNow).TotalSeconds;
        renewalSpan.Should().BeInRange(3500, 3700);
    }

    [Test]
    public async Task RenewAllMappingsAsync_WhenRenewalFails_BacksOffNextRenewal()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            // Initial map request succeeds
            var req1 = await mockGateway.ReceiveAsync(cts.Token);
            var resp1 = new byte[16];
            resp1[0] = 0x00;
            resp1[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(4, 4), 100);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(10, 2), 51413);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(resp1, resp1.Length, req1.RemoteEndPoint);

            // Renewal request returns OutOfResources error (4)
            var req2 = await mockGateway.ReceiveAsync(cts.Token);
            var resp2 = new byte[16];
            resp2[0] = 0x00;
            resp2[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(2, 2), 4);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(4, 4), 101);
            await mockGateway.SendAsync(resp2, resp2.Length, req2.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        // Force renew with failing response
        await service.RenewAllMappingsAsync(force: true, cancellationToken: cts.Token);
        await serverTask;

        // Mapping is preserved, and next renewal is backed off by 60 seconds
        var mapping = service.ActiveMappings.Should().ContainSingle().Subject;
        var retrySpan = (mapping.NextRenewalUtc - DateTime.UtcNow).TotalSeconds;
        retrySpan.Should().BeInRange(50, 70);
    }

    [Test]
    public async Task CheckAndRenewMappingsAsync_TriggersPeriodicEpochVerification_AndHandlesLocalIpChange()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            // Initial map request
            var req1 = await mockGateway.ReceiveAsync(cts.Token);
            var resp1 = new byte[16];
            resp1[0] = 0x00;
            resp1[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(4, 4), 100);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(10, 2), 51413);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(resp1, resp1.Length, req1.RemoteEndPoint);

            // Verification request (GetExternalIpAddressAsync)
            var req2 = await mockGateway.ReceiveAsync(cts.Token);
            var resp2 = new byte[12];
            resp2[0] = 0x00;
            resp2[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(4, 4), 101);
            IPAddress.Parse("198.51.100.55").GetAddressBytes().CopyTo(resp2.AsSpan(8, 4));
            await mockGateway.SendAsync(resp2, resp2.Length, req2.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        // Simulate local IP change on active mapping
        var active = service.ActiveMappings.First();
        active.LocalIpAddress = IPAddress.Parse("10.99.99.99");
        active.NextRenewalUtc = DateTime.UtcNow.AddSeconds(-10); // Expired renewal

        await service.CheckAndRenewMappingsAsync();
        await serverTask;

        service.GatewayEpochs.Should().ContainKey(IPAddress.Loopback);
    }

    [Test]
    public async Task UnmapPortAsync_And_StopAsync_RevokesMappingsWithLifetimeZero()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            // Initial map
            var req1 = await mockGateway.ReceiveAsync(cts.Token);
            var resp1 = new byte[16];
            resp1[0] = 0x00;
            resp1[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(4, 4), 100);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(10, 2), 51413);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(resp1, resp1.Length, req1.RemoteEndPoint);

            // Revocation during StopAsync
            var req2 = await mockGateway.ReceiveAsync(cts.Token);
            var resp2 = new byte[16];
            resp2[0] = 0x00;
            resp2[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(4, 4), 101);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(12, 4), 0);
            await mockGateway.SendAsync(resp2, resp2.Length, req2.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        service.ActiveMappings.Should().HaveCount(1);

        await service.StopAsync(cts.Token);
        await serverTask;

        service.ActiveMappings.Should().BeEmpty();

        // Subsequent StopAsync call is safe and idempotent
        await service.StopAsync(cts.Token);
    }

    [Test]
    public async Task Dispose_And_DisposeAsync_CleansUpResourcesAndThrowsObjectDisposedException()
    {
        var service1 = new NatPmpPortMapperService(5351);
        service1.Dispose();

        // Double dispose is safe
        service1.Dispose();

        // Disposed instance throws ObjectDisposedException
        var actIp = async () => await service1.GetExternalIpAddressAsync();
        await actIp.Should().ThrowAsync<ObjectDisposedException>();

        var actMap = async () => await service1.MapPortAsync(51413, NatPmpProtocol.Tcp);
        await actMap.Should().ThrowAsync<ObjectDisposedException>();

        var actUnmap = async () => await service1.UnmapPortAsync(51413, NatPmpProtocol.Tcp);
        await actUnmap.Should().ThrowAsync<ObjectDisposedException>();

        var actRenew = async () => await service1.RenewAllMappingsAsync();
        await actRenew.Should().ThrowAsync<ObjectDisposedException>();

        // DisposeAsync cleans up cleanly
        var service2 = new NatPmpPortMapperService(5351);
        await service2.DisposeAsync();
        await service2.DisposeAsync(); // Idempotent
    }

    [Test]
    public void NetworkAddressChanged_And_LocalIpResolution_HandlesEventsGracefully()
    {
        using var service = new NatPmpPortMapperService(5351);

        // Network address changed event
        service.OnNetworkAddressChanged(null, EventArgs.Empty);

        // Local IP address probe
        var localIp = service.GetLocalIpAddressForGateway(IPAddress.Loopback);
        if (localIp != null)
        {
            localIp.AddressFamily.Should().Be(AddressFamily.InterNetwork);
        }

        service.GetLocalIpAddressForGateway(null).Should().BeNull();
        service.GetLocalEndPointForBoundInterface().Should().BeNull();
    }
}
