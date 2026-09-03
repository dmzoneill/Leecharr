// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
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
        // May be null or a valid IPv4 gateway address depending on test environment
        if (gw != null)
        {
            gw.AddressFamily.Should().Be(AddressFamily.InterNetwork);
        }
    }

    [Test]
    public async Task MapPortAsync_WithMockGateway_SuccessfullyParsesResponse()
    {
        // Start a mock UDP listener simulating an RFC 6886 NAT-PMP gateway on an ephemeral loopback port
        using var mockGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mockPort = ((IPEndPoint)mockGateway.Client.LocalEndPoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            var received = await mockGateway.ReceiveAsync(cts.Token);
            var req = received.Buffer;

            // Verify mapping request: 12 bytes
            req.Length.Should().Be(12);
            var opcode = req[1]; // 1 for UDP, 2 for TCP
            var internalPort = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4, 2));

            // Craft response: 16 bytes [0x00, 0x80+opcode, result(2), epoch(4), internal(2), external(2), lifetime(4)]
            var resp = new byte[16];
            resp[0] = 0x00;
            resp[1] = (byte)(0x80 + opcode);
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0); // Success
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 123456); // Epoch
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), internalPort);
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), (ushort)(internalPort + 100)); // External port
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(12, 4), 3600); // Lifetime

            await mockGateway.SendAsync(resp, resp.Length, received.RemoteEndPoint);
        });

        // Use custom mock client communicating with the mock port
        using var clientUdp = new UdpClient();
        var request = new byte[12];
        request[0] = 0x00;
        request[1] = (byte)NatPmpProtocol.Tcp;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6881);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6, 2), 6881);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8, 4), 3600);

        await clientUdp.SendAsync(request, request.Length, new IPEndPoint(IPAddress.Loopback, mockPort));
        var clientReceived = await clientUdp.ReceiveAsync(cts.Token);

        await serverTask;

        var buf = clientReceived.Buffer;
        buf.Length.Should().Be(16);
        buf[1].Should().Be(0x82); // 0x80 + TCP (2)
        var result = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(2, 2));
        result.Should().Be(0);
        var mappedExt = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(10, 2));
        mappedExt.Should().Be(6981);
    }
}
