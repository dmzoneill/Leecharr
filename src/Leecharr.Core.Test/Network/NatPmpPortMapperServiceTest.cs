// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
}
