// Copyright (c) PlaceholderCompany. All rights reserved.

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
using NzbDrone.Core.Network.PortMapping;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NatPmpPortMapperServiceTest
{
    [Test]
    public void DiscoverDefaultGateway_DoesNotThrow()
    {
        var gw = NatPmpPortMapperService.DiscoverDefaultGateway();
        if (gw != null)
        {
            gw.AddressFamily.Should().Be(AddressFamily.InterNetwork);
        }
    }

    [Test]
    public void DiscoverDefaultGateway_WithBoundInterface_DoesNotThrow()
    {
        var gw = NatPmpPortMapperService.DiscoverDefaultGateway("tun0");
        if (gw != null)
        {
            gw.AddressFamily.Should().Be(AddressFamily.InterNetwork);
        }
    }

    [Test]
    public void Constructor_WithBoundInterfaceAndConfigService_InitializesCorrectly()
    {
        var config = NSubstitute.Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        config.BindInterface.Returns("tun0");

        using var service = new NatPmpPortMapperService(5351, "tun0", config);
        service.ActiveMappings.Should().BeEmpty();
    }

    [Test]
    public async Task MapPortAsync_WithMockGateway_SuccessfullyParsesResponse_AndTracksActiveMapping()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var received = await mockGateway.ReceiveAsync(cts.Token);
            var req = received.Buffer;

            req.Length.Should().Be(12);
            var opcode = req[1];
            var internalPort = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4, 2));

            var resp = new byte[16];
            resp[0] = 0x00;
            resp[1] = (byte)(0x80 + opcode);
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0); // Result = 0
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 123456); // Epoch
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), internalPort);
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), (ushort)(internalPort + 100)); // Mapped external port
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 3600); // 1 hour lifetime

            await mockGateway.SendAsync(resp, resp.Length, received.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, suggestedExternalPort: 51413, lifetimeSeconds: 3600, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        await serverTask;

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.InternalPort.Should().Be(51413);
        result.ExternalPort.Should().Be(51513);
        result.LifetimeSeconds.Should().Be(3600);

        // Verify active mappings tracking
        service.ActiveMappings.Should().HaveCount(1);
        var active = service.ActiveMappings.Should().ContainSingle().Subject;
        active.InternalPort.Should().Be(51413);
        active.Protocol.Should().Be(NatPmpProtocol.Tcp);
        active.ExternalPort.Should().Be(51513);
        active.LifetimeSeconds.Should().Be(3600);

        // Renewal must be scheduled at 50% lifetime (RFC 6886: ~1800s in future)
        var secondsUntilRenewal = (active.NextRenewalUtc - DateTime.UtcNow).TotalSeconds;
        secondsUntilRenewal.Should().BeInRange(1750, 1850);
    }

    [Test]
    public async Task RenewAllMappingsAsync_RenewsActiveMappings_AndRefreshesNextRenewal()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var receivedRequests = new List<byte[]>();

        var serverTask = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var received = await mockGateway.ReceiveAsync(cts.Token);
                receivedRequests.Add(received.Buffer);

                var req = received.Buffer;
                var opcode = req[1];
                var internalPort = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4, 2));

                var resp = new byte[16];
                resp[0] = 0x00;
                resp[1] = (byte)(0x80 + opcode);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 1000);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), internalPort);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 52000);
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 3600);

                await mockGateway.SendAsync(resp, resp.Length, received.RemoteEndPoint);
            }
        });

        using var service = new NatPmpPortMapperService(mockPort);

        // 1. Initial mapping
        var initialResult = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, suggestedExternalPort: 52000, lifetimeSeconds: 3600, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        initialResult.Success.Should().BeTrue();

        // 2. Trigger renewal
        await service.RenewAllMappingsAsync(force: true, cancellationToken: cts.Token);

        await serverTask;

        receivedRequests.Should().HaveCount(2);

        // Verify the renewal request used the mapped external port and requested full lifetime
        var renewalReq = receivedRequests[1];
        var renewalInternal = BinaryPrimitives.ReadUInt16BigEndian(renewalReq.AsSpan(4, 2));
        var renewalExternal = BinaryPrimitives.ReadUInt16BigEndian(renewalReq.AsSpan(6, 2));
        var renewalLifetime = BinaryPrimitives.ReadUInt32BigEndian(renewalReq.AsSpan(8, 4));

        renewalInternal.Should().Be(51413);
        renewalExternal.Should().Be(52000);
        renewalLifetime.Should().Be(3600);

        // Verify next renewal was refreshed
        var active = service.ActiveMappings.Should().ContainSingle().Subject;
        var secondsUntilRenewal = (active.NextRenewalUtc - DateTime.UtcNow).TotalSeconds;
        secondsUntilRenewal.Should().BeInRange(1750, 1850);
    }

    [Test]
    public async Task UnmapPortAsync_SendsZeroLifetimeAndRemovesMapping()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        byte[] unmapRequest = null;

        var serverTask = Task.Run(async () =>
        {
            // Initial map
            var req1 = await mockGateway.ReceiveAsync(cts.Token);
            var resp1 = new byte[16];
            resp1[0] = 0x00;
            resp1[1] = 0x82; // TCP
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(4, 4), 100);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(8, 2), 6881);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(10, 2), 6881);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(resp1, resp1.Length, req1.RemoteEndPoint);

            // Unmap request (lifetime = 0)
            var req2 = await mockGateway.ReceiveAsync(cts.Token);
            unmapRequest = req2.Buffer;
            var resp2 = new byte[16];
            resp2[0] = 0x00;
            resp2[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(4, 4), 101);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(8, 2), 6881);
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp2.AsSpan(12, 4), 0); // Lifetime 0
            await mockGateway.SendAsync(resp2, resp2.Length, req2.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        await service.MapPortAsync(6881, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        service.ActiveMappings.Should().HaveCount(1);

        var unmapped = await service.UnmapPortAsync(6881, NatPmpProtocol.Tcp, IPAddress.Loopback, cts.Token);
        await serverTask;

        unmapped.Should().BeTrue();
        service.ActiveMappings.Should().BeEmpty();

        unmapRequest.Should().NotBeNull();
        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(unmapRequest.AsSpan(8, 4));
        lifetime.Should().Be(0); // Lifetime 0 per RFC 6886
    }

    [Test]
    public async Task StopAsync_RevokesAllActiveMappingsWithZeroLifetime()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var revokedRequests = new List<byte[]>();

        var serverTask = Task.Run(async () =>
        {
            // Handle 2 initial map requests (TCP + UDP)
            for (var i = 0; i < 2; i++)
            {
                var req = await mockGateway.ReceiveAsync(cts.Token);
                var opcode = req.Buffer[1];
                var resp = new byte[16];
                resp[0] = 0x00;
                resp[1] = (byte)(0x80 + opcode);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 50);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), 51413);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 51413);
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 3600);
                await mockGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
            }

            // Handle 2 revocation requests (lifetime = 0)
            for (var i = 0; i < 2; i++)
            {
                var req = await mockGateway.ReceiveAsync(cts.Token);
                revokedRequests.Add(req.Buffer);
                var opcode = req.Buffer[1];
                var resp = new byte[16];
                resp[0] = 0x00;
                resp[1] = (byte)(0x80 + opcode);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 51);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), 51413);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 0);
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 0);
                await mockGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
            }
        });

        using var service = new NatPmpPortMapperService(mockPort);
        await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        await service.MapPortAsync(51413, NatPmpProtocol.Udp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        service.ActiveMappings.Should().HaveCount(2);

        await service.StopAsync(cts.Token);
        await serverTask;

        service.ActiveMappings.Should().BeEmpty();
        revokedRequests.Should().HaveCount(2);

        foreach (var req in revokedRequests)
        {
            var lifetime = BinaryPrimitives.ReadUInt32BigEndian(req.AsSpan(8, 4));
            lifetime.Should().Be(0); // RFC 6886: revocation sets lifetime to 0
        }
    }

    [Test]
    public async Task MapPortAsync_Cancellation_HandlesCleanlyWithoutObjectDisposedException()
    {
        // No server responding
        using var service = new NatPmpPortMapperService(59999);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
    }

    [Test]
    public async Task GetExternalIpAddressAsync_WithMockGateway_ReturnsExternalIp()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var received = await mockGateway.ReceiveAsync(cts.Token);
            var req = received.Buffer;

            // Opcode 0 request: 2 bytes [0x00, 0x00]
            req.Length.Should().Be(2);
            req[0].Should().Be(0x00);
            req[1].Should().Be(0x00);

            // 12 bytes response [0x00, 0x80, result(2), epoch(4), ip(4)]
            var resp = new byte[12];
            resp[0] = 0x00;
            resp[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 999);
            resp[8] = 203;
            resp[9] = 0;
            resp[10] = 113;
            resp[11] = 42;

            await mockGateway.SendAsync(resp, resp.Length, received.RemoteEndPoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var ip = await service.GetExternalIpAddressAsync(IPAddress.Loopback, cts.Token);

        await serverTask;

        ip.Should().NotBeNull();
        ip.ToString().Should().Be("203.0.113.42");
    }

    [Test]
    public async Task GetExternalIpAddressAsync_Cancellation_HandlesCleanlyWithoutException()
    {
        using var service = new NatPmpPortMapperService(59999);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var ip = await service.GetExternalIpAddressAsync(IPAddress.Loopback, cts.Token);

        ip.Should().BeNull();
    }

    [Test]
    public async Task MapPortAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var service = new NatPmpPortMapperService(5351);
        service.Dispose();

        var act = async () => await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Test]
    public async Task UnmapPortAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var service = new NatPmpPortMapperService(5351);
        service.Dispose();

        var act = async () => await service.UnmapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Test]
    public async Task GetExternalIpAddressAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var service = new NatPmpPortMapperService(5351);
        service.Dispose();

        var act = async () => await service.GetExternalIpAddressAsync(IPAddress.Loopback);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Test]
    public async Task RenewAllMappingsAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var service = new NatPmpPortMapperService(5351);
        service.Dispose();

        var act = async () => await service.RenewAllMappingsAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Test]
    public void Dispose_MultipleCallsAndConcurrentCalls_DoesNotThrow()
    {
        var service = new NatPmpPortMapperService(5351);

        // Multiple sequential calls
        service.Dispose();
        service.Dispose();

        // Concurrent dispose calls
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() => service.Dispose())).ToArray();
        Assert.DoesNotThrow(() => Task.WaitAll(tasks));
    }

    [Test]
    public async Task DisposeAsync_MultipleCallsAndConcurrentCalls_DoesNotThrow()
    {
        var service = new NatPmpPortMapperService(5351);

        // Multiple sequential calls
        await service.DisposeAsync();
        await service.DisposeAsync();

        // Concurrent dispose calls
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () => await service.DisposeAsync())).ToArray();
        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public void SelectBestGateway_WhenPhysicalEthernetPresent_PrioritizesPhysicalGatewayOverDockerAndVirtualBridges()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "docker0",
                Description: "Docker Bridge",
                Id: "docker0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("172.17.0.1"), IPAddress.Parse("255.255.0.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("172.17.0.1") }),
            new(
                Name: "vboxnet0",
                Description: "VirtualBox Host-Only Ethernet Adapter",
                Id: "vboxnet0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("192.168.56.1"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.56.1") }),
            new(
                Name: "eth0",
                Description: "Intel Ethernet Connection",
                Id: "eth0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("192.168.1.100"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.1.1") }),
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("192.168.1.1"));
    }

    [Test]
    public void SelectBestGateway_WhenWireless80211Present_PrioritizesOverHyperVAndWsl()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "vEthernet (WSL)",
                Description: "Hyper-V Virtual Ethernet Adapter",
                Id: "wsl0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("172.28.0.1"), IPAddress.Parse("255.255.240.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("172.28.0.1") }),
            new(
                Name: "wlan0",
                Description: "Intel Wi-Fi 6 AX200",
                Id: "wlan0",
                InterfaceType: NetworkInterfaceType.Wireless80211,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("10.0.0.50"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("10.0.0.1") }),
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("10.0.0.1"));
    }

    [Test]
    public void SelectBestGateway_WithBoundInterface_PrioritizesBoundInterfaceGateway()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "eth0",
                Description: "Intel Ethernet",
                Id: "eth0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("192.168.1.100"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.1.1") }),
            new(
                Name: "tun0",
                Description: "WireGuard Tunnel",
                Id: "tun0",
                InterfaceType: NetworkInterfaceType.Tunnel,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("10.8.0.2"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("10.8.0.1") }),
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates, boundInterface: "tun0");

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("10.8.0.1"));
    }

    [Test]
    public void SelectBestGateway_WhenOnlyVirtualCandidatesExist_FallsBackToNonDockerVirtualGateway()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "docker0",
                Description: "Docker Bridge",
                Id: "docker0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("172.17.0.1"), IPAddress.Parse("255.255.0.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("172.17.0.1") }),
            new(
                Name: "vmnet1",
                Description: "VMware Network Adapter VMnet1",
                Id: "vmnet1",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("192.168.100.1"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.100.1") }),
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("192.168.100.1"));
    }

    [Test]
    public void SelectBestGateway_WhenOnlyDockerBridgeExists_FallsBackToDockerBridge()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "docker0",
                Description: "Docker Bridge",
                Id: "docker0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("172.17.0.2"), IPAddress.Parse("255.255.0.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("172.17.0.1") }),
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("172.17.0.1"));
    }

    [Test]
    public void SelectBestGateway_FiltersOutApipaAddresses()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "eth0",
                Description: "Unconfigured Ethernet",
                Id: "eth0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("169.254.10.20"), IPAddress.Parse("255.255.0.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("169.254.10.1") }),
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);

        selected.Should().BeNull();
    }

    [Test]
    public void SelectBestGateway_FiltersOutDownAndLoopbackInterfaces()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "lo",
                Description: "Loopback Interface",
                Id: "lo",
                InterfaceType: NetworkInterfaceType.Loopback,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Loopback, IPAddress.Parse("255.0.0.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Loopback }),
            new(
                Name: "eth0",
                Description: "Disconnected Ethernet",
                Id: "eth0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Down,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("192.168.1.50"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.1.1") }),
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);

        selected.Should().BeNull();
    }

    [Test]
    public void SelectBestGateway_WithMultiplePhysicalAdapters_PrioritizesSubnetMatchingGateway()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>
        {
            new(
                Name: "eth0",
                Description: "Intel Ethernet 0",
                Id: "eth0",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("10.0.0.50"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("172.16.0.1") }), // Gateway not on subnet
            new(
                Name: "eth1",
                Description: "Intel Ethernet 1",
                Id: "eth1",
                InterfaceType: NetworkInterfaceType.Ethernet,
                OperationalStatus: OperationalStatus.Up,
                UnicastAddresses: new List<(IPAddress, IPAddress)> { (IPAddress.Parse("192.168.1.50"), IPAddress.Parse("255.255.255.0")) },
                GatewayAddresses: new List<IPAddress> { IPAddress.Parse("192.168.1.1") }), // Gateway matches subnet
        };

        var selected = NatPmpPortMapperService.SelectBestGateway(candidates);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("192.168.1.1"));
    }

    [Test]
    public void SelectBestGateway_WhenNullOrEmpty_ReturnsNull()
    {
        NatPmpPortMapperService.SelectBestGateway(null).Should().BeNull();
        NatPmpPortMapperService.SelectBestGateway(Enumerable.Empty<NatPmpNetworkInterfaceCandidate>()).Should().BeNull();
    }

    [Test]
    [TestCase("docker0", "", true)]
    [TestCase("veth9876", "", true)]
    [TestCase("vmnet8", "", true)]
    [TestCase("vboxnet0", "", true)]
    [TestCase("eth0", "VirtualBox Host-Only Ethernet Adapter", true)]
    [TestCase("eth0", "Hyper-V Virtual Ethernet Adapter", true)]
    [TestCase("vEthernet (WSL)", "", true)]
    [TestCase("virbr0", "", true)]
    [TestCase("tailscale0", "", true)]
    [TestCase("cni0", "", true)]
    [TestCase("eth0", "Intel(R) Ethernet Connection I219-V", false)]
    [TestCase("wlan0", "Intel(R) Wi-Fi 6 AX200 160MHz", false)]
    [TestCase("enp3s0", "", false)]
    public void IsVirtualInterfaceName_IdentifiesVirtualPatternsCorrectly(string name, string description, bool expected)
    {
        NatPmpPortMapperService.IsVirtualInterfaceName(name, description).Should().Be(expected);
    }

    [Test]
    public void IsPhysicalInterfaceType_IdentifiesPhysicalTypes()
    {
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Ethernet).Should().BeTrue();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Wireless80211).Should().BeTrue();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.GigabitEthernet).Should().BeTrue();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Tunnel).Should().BeFalse();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Ppp).Should().BeFalse();
        NatPmpPortMapperService.IsPhysicalInterfaceType(NetworkInterfaceType.Loopback).Should().BeFalse();
    }

    [Test]
    public void IsValidIpv4UnicastAddress_And_IsValidGatewayAddress_ValidatesCorrectly()
    {
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("192.168.1.50")).Should().BeTrue();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("10.0.0.1")).Should().BeTrue();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Loopback).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Any).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.None).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.Parse("169.254.1.1")).Should().BeFalse();
        NatPmpPortMapperService.IsValidIpv4UnicastAddress(IPAddress.IPv6Loopback).Should().BeFalse();

        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("192.168.1.1")).Should().BeTrue();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Loopback).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Any).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.None).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.Parse("169.254.1.1")).Should().BeFalse();
        NatPmpPortMapperService.IsValidGatewayAddress(IPAddress.IPv6Loopback).Should().BeFalse();
    }

    [Test]
    public void IsInSameSubnet_CalculatesSubnetReachabilityCorrectly()
    {
        var mask = IPAddress.Parse("255.255.255.0");
        NatPmpPortMapperService.IsInSameSubnet(IPAddress.Parse("192.168.1.50"), IPAddress.Parse("192.168.1.1"), mask).Should().BeTrue();
        NatPmpPortMapperService.IsInSameSubnet(IPAddress.Parse("192.168.1.50"), IPAddress.Parse("192.168.2.1"), mask).Should().BeFalse();
        NatPmpPortMapperService.IsInSameSubnet(IPAddress.Parse("10.0.1.50"), IPAddress.Parse("10.0.2.1"), IPAddress.Parse("255.255.0.0")).Should().BeTrue();
        NatPmpPortMapperService.IsInSameSubnet(IPAddress.Parse("10.0.1.50"), IPAddress.Parse("10.0.2.1"), IPAddress.Parse("0.0.0.0")).Should().BeFalse();
    }

    [Test]
    public void TrackEpoch_TracksEpochPerGatewayIPIndependently()
    {
        using var service = new NatPmpPortMapperService();
        var gw1 = IPAddress.Parse("192.168.1.1");
        var gw2 = IPAddress.Parse("10.0.0.1");

        // Set initial epochs on two separate gateways
        service.TrackEpoch(gw1, 1000);
        service.TrackEpoch(gw2, 500);

        service.GatewayEpochs.Should().ContainKey(gw1).WhoseValue.Should().Be(1000);
        service.GatewayEpochs.Should().ContainKey(gw2).WhoseValue.Should().Be(500);

        // Update gw1 with higher epoch, gw2 with higher epoch
        service.TrackEpoch(gw1, 1050);
        service.TrackEpoch(gw2, 600);

        service.GatewayEpochs[gw1].Should().Be(1050);
        service.GatewayEpochs[gw2].Should().Be(600);

        // Update gw1 to lower epoch (reboot on gw1), gw2 should remain unaffected
        service.TrackEpoch(gw1, 10);
        service.GatewayEpochs[gw1].Should().Be(10);
        service.GatewayEpochs[gw2].Should().Be(600);
    }

    [Test]
    public async Task TrackEpoch_OnGatewayReboot_DeduplicatesConcurrentRenewalTriggers()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var totalRequestsReceived = 0;

        var serverTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var received = await mockGateway.ReceiveAsync(cts.Token);
                    Interlocked.Increment(ref totalRequestsReceived);

                    var req = received.Buffer;
                    var opcode = req[1];
                    var internalPort = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4, 2));

                    var resp = new byte[16];
                    resp[0] = 0x00;
                    resp[1] = (byte)(0x80 + opcode);
                    BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
                    // Return new low epoch (rebooted state)
                    BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 10);
                    BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), internalPort);
                    BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), internalPort);
                    BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 3600);

                    await mockGateway.SendAsync(resp, resp.Length, received.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        using var service = new NatPmpPortMapperService(mockPort);

        // Set initial high epoch (e.g. before reboot)
        service.TrackEpoch(IPAddress.Loopback, 100000);

        // 1. Initial mapping for TCP and UDP (2 requests)
        var res1 = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        var res2 = await service.MapPortAsync(51413, NatPmpProtocol.Udp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        res1.Success.Should().BeTrue();
        res2.Success.Should().BeTrue();

        service.ActiveMappings.Should().HaveCount(2);

        // Reset high epoch to simulate router rebooting afterwards
        service.TrackEpoch(IPAddress.Loopback, 100000);

        // Simulate 5 concurrent responses observing the decreased epoch (e.g. epoch 5 < 100000)
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => service.TrackEpoch(IPAddress.Loopback, 5)))
            .ToArray();
        await Task.WhenAll(tasks);

        // Wait a short delay for background coordinated renewal to complete
        await Task.Delay(300, CancellationToken.None);

        // Cancel mock gateway listener
        cts.Cancel();
        await serverTask;

        // Total requests: 2 initial maps + 2 coordinated reboot renewals = 4 total requests.
        // If deduplication failed, multiple background tasks would have queued up resulting in >4 requests.
        totalRequestsReceived.Should().Be(4);
    }

    [Test]
    public async Task TrackEpoch_DuringForceRenewal_DoesNotTriggerCascadingRenewalTasks()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var totalRequestsReceived = 0;

        var serverTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var received = await mockGateway.ReceiveAsync(cts.Token);
                    Interlocked.Increment(ref totalRequestsReceived);

                    var req = received.Buffer;
                    var opcode = req[1];
                    var internalPort = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4, 2));

                    var resp = new byte[16];
                    resp[0] = 0x00;
                    resp[1] = (byte)(0x80 + opcode);
                    BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0);
                    // Return low epoch indicating reboot
                    BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 20);
                    BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), internalPort);
                    BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), internalPort);
                    BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 3600);

                    await mockGateway.SendAsync(resp, resp.Length, received.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        using var service = new NatPmpPortMapperService(mockPort);

        // Initial mappings
        await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);
        await service.MapPortAsync(51413, NatPmpProtocol.Udp, gateway: IPAddress.Loopback, cancellationToken: cts.Token);

        // Set high epoch before force renewal
        service.TrackEpoch(IPAddress.Loopback, 50000);

        // Execute force renewal directly - responses will have lower epoch 20
        await service.RenewAllMappingsAsync(force: true, cancellationToken: cts.Token);

        // Allow any background tasks to attempt execution if scheduled erroneously
        await Task.Delay(300, CancellationToken.None);

        cts.Cancel();
        await serverTask;

        // 2 initial map requests + 2 force renewal requests = 4 total requests.
        // No redundant cascading renewal runs after the force renewal.
        totalRequestsReceived.Should().Be(4);
    }

    [Test]
    public async Task GetExternalIpAddressAsync_WithIPv6Gateway_ReturnsNullSafely()
    {
        using var service = new NatPmpPortMapperService();
        var result = await service.GetExternalIpAddressAsync(IPAddress.IPv6Loopback);
        result.Should().BeNull();
    }

    [Test]
    public async Task MapPortAsync_WithIPv6Gateway_ReturnsFailureSafely()
    {
        using var service = new NatPmpPortMapperService();
        var result = await service.MapPortAsync(51413, NatPmpProtocol.Tcp, gateway: IPAddress.IPv6Loopback);
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("IPv4");
    }

    [Test]
    public async Task SendAndReceive_DiscardsPacketsFromNonGatewayEndpoint_AndAcceptsValidGatewayResponse()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var rogueSender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var received = await mockGateway.ReceiveAsync(cts.Token);
            var clientEndpoint = received.RemoteEndPoint;

            // 1. Send forged packet from rogue sender with spoofed IP (1.2.3.4) in buffer
            var spoofedResp = new byte[12];
            spoofedResp[0] = 0x00;
            spoofedResp[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(spoofedResp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(spoofedResp.AsSpan(4, 4), 100);
            spoofedResp[8] = 1;
            spoofedResp[9] = 2;
            spoofedResp[10] = 3;
            spoofedResp[11] = 4;
            await rogueSender.SendAsync(spoofedResp, spoofedResp.Length, clientEndpoint);

            // Give a tiny moment before sending legitimate gateway response
            await Task.Delay(50, cts.Token);

            // 2. Send legitimate packet from expected gateway with real IP (203.0.113.10)
            var legitResp = new byte[12];
            legitResp[0] = 0x00;
            legitResp[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(legitResp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(legitResp.AsSpan(4, 4), 100);
            legitResp[8] = 203;
            legitResp[9] = 0;
            legitResp[10] = 113;
            legitResp[11] = 10;
            await mockGateway.SendAsync(legitResp, legitResp.Length, clientEndpoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var ip = await service.GetExternalIpAddressAsync(IPAddress.Loopback, cts.Token);

        await serverTask;

        ip.Should().NotBeNull();
        ip.ToString().Should().Be("203.0.113.10");
    }

    [Test]
    public async Task SendAndReceive_DiscardsPacketsWithNonMatchingOpcode_AndAcceptsValidMatchingResponse()
    {
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var received = await mockGateway.ReceiveAsync(cts.Token);
            var clientEndpoint = received.RemoteEndPoint;

            // 1. Send packet with non-matching opcode (0x82 instead of 0x80)
            var strayResp = new byte[16];
            strayResp[0] = 0x00;
            strayResp[1] = 0x82;
            BinaryPrimitives.WriteUInt16BigEndian(strayResp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(strayResp.AsSpan(4, 4), 100);
            BinaryPrimitives.WriteUInt16BigEndian(strayResp.AsSpan(8, 2), 51413);
            BinaryPrimitives.WriteUInt16BigEndian(strayResp.AsSpan(10, 2), 51413);
            BinaryPrimitives.WriteUInt32BigEndian(strayResp.AsSpan(12, 4), 3600);
            await mockGateway.SendAsync(strayResp, strayResp.Length, clientEndpoint);

            // Give a tiny moment before sending legitimate opcode response
            await Task.Delay(50, cts.Token);

            // 2. Send expected opcode 0x80 response
            var legitResp = new byte[12];
            legitResp[0] = 0x00;
            legitResp[1] = 0x80;
            BinaryPrimitives.WriteUInt16BigEndian(legitResp.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(legitResp.AsSpan(4, 4), 100);
            legitResp[8] = 198;
            legitResp[9] = 51;
            legitResp[10] = 100;
            legitResp[11] = 20;
            await mockGateway.SendAsync(legitResp, legitResp.Length, clientEndpoint);
        });

        using var service = new NatPmpPortMapperService(mockPort);
        var ip = await service.GetExternalIpAddressAsync(IPAddress.Loopback, cts.Token);

        await serverTask;

        ip.Should().NotBeNull();
        ip.ToString().Should().Be("198.51.100.20");
    }
}
