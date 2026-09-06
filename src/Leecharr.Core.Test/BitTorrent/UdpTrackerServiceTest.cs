// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class UdpTrackerServiceTest
{
    private IEmbeddedTrackerService trackerService;
    private IConfigService configService;
    private UdpTrackerService udpTrackerService;

    [SetUp]
    public void SetUp()
    {
        this.trackerService = Substitute.For<IEmbeddedTrackerService>();
        this.configService = Substitute.For<IConfigService>();

        this.configService.TrackerServerEnabled.Returns(true);
        this.configService.TrackerUdpEnabled.Returns(true);
        this.configService.TrackerUdpPort.Returns(0); // dynamic port for testing
        this.configService.TrackerBindAddress.Returns("127.0.0.1");
        this.configService.TrackerAnnounceInterval.Returns(1800);
        this.configService.TrackerMaxPeersPerAnnounce.Returns(50);

        this.udpTrackerService = new UdpTrackerService(this.trackerService, this.configService);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (this.udpTrackerService != null)
        {
            await this.udpTrackerService.StopAsync();
            this.udpTrackerService.Dispose();
        }
    }

    [Test]
    public void HandlePacket_ShortPacket_ReturnsNull()
    {
        var shortPacket = new byte[15];
        var result = this.udpTrackerService.HandlePacket(shortPacket, new IPEndPoint(IPAddress.Loopback, 12345));
        result.Should().BeNull();
    }

    [Test]
    public void HandlePacket_Connect_ValidProtocolId_ReturnsConnectResponse()
    {
        var packet = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(0, 8), 0x41727101980L);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8, 4), 0); // Action = Connect
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12, 4), 98765); // TransactionId

        var response = this.udpTrackerService.HandlePacket(packet, new IPEndPoint(IPAddress.Loopback, 12345));
        response.Should().NotBeNull();
        response.Length.Should().Be(16);

        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(0); // Action = 0
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(98765); // TransactionId
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(response.AsSpan(8, 8));
        connectionId.Should().NotBe(0);
    }

    [Test]
    public void HandlePacket_Connect_InvalidProtocolId_ReturnsErrorResponse()
    {
        var packet = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(0, 8), 0x12345678L);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12, 4), 112233);

        var response = this.udpTrackerService.HandlePacket(packet, new IPEndPoint(IPAddress.Loopback, 12345));
        response.Should().NotBeNull();
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(3); // Action = Error
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(112233);
        Encoding.UTF8.GetString(response.AsSpan(8)).Should().Contain("Invalid protocol_id");
    }

    [Test]
    public void HandlePacket_Announce_ValidRequest_ReturnsAnnounceResponseWithPeers()
    {
        // 1. Connect first to obtain a valid connectionId
        var connectPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), 0x41727101980L);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), 1);

        var connectResp = this.udpTrackerService.HandlePacket(connectPacket, new IPEndPoint(IPAddress.Loopback, 12345));
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResp.AsSpan(8, 8));

        // 2. Prepare mock response from trackerService.Announce
        var infoHash = new byte[20];
        infoHash[0] = 0xAA;
        var peerId = new byte[20];
        peerId[0] = 0xBB;

        var peer1 = new TrackerPeerState
        {
            Ip = IPAddress.Parse("192.168.1.50"),
            Port = 6881,
            Left = 0,
        };
        var peer2 = new TrackerPeerState
        {
            Ip = IPAddress.Parse("10.0.0.1"),
            Port = 6882,
            Left = 500,
        };

        this.trackerService.Announce(Arg.Any<TrackerAnnounceRequest>()).Returns(new TrackerAnnounceResult
        {
            Success = true,
            Interval = 1800,
            Leechers = 1,
            Seeders = 1,
            Peers = new List<TrackerPeerState> { peer1, peer2 },
        });

        // 3. Build announce packet (98 bytes)
        var announcePacket = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(8, 4), 1); // Action = Announce
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(12, 4), 42); // TransactionId
        infoHash.CopyTo(announcePacket.AsSpan(16, 20));
        peerId.CopyTo(announcePacket.AsSpan(36, 20));
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(56, 8), 100); // downloaded
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(64, 8), 200); // left
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(72, 8), 300); // uploaded
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(80, 4), 2); // event = 2 (started)
        BinaryPrimitives.WriteUInt32BigEndian(announcePacket.AsSpan(84, 4), 0); // ip = 0 (default)
        BinaryPrimitives.WriteUInt32BigEndian(announcePacket.AsSpan(88, 4), 999); // key
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(92, 4), 50); // num_want
        BinaryPrimitives.WriteUInt16BigEndian(announcePacket.AsSpan(96, 2), 6881); // port

        var response = this.udpTrackerService.HandlePacket(announcePacket, new IPEndPoint(IPAddress.Loopback, 12345));
        response.Should().NotBeNull();
        response.Length.Should().Be(20 + (2 * 6)); // 20 header + 12 peers

        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(1); // Action = 1 (Announce)
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(42); // TransactionId
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(8, 4)).Should().Be(1800); // Interval
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(12, 4)).Should().Be(1); // Leechers
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(16, 4)).Should().Be(1); // Seeders

        // Peer 1
        var p1Ip = new IPAddress(response.AsSpan(20, 4));
        p1Ip.ToString().Should().Be("192.168.1.50");
        BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(24, 2)).Should().Be(6881);

        // Peer 2
        var p2Ip = new IPAddress(response.AsSpan(26, 4));
        p2Ip.ToString().Should().Be("10.0.0.1");
        BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(30, 2)).Should().Be(6882);
    }

    [Test]
    public void HandlePacket_Announce_InvalidConnectionId_ReturnsErrorResponse()
    {
        var announcePacket = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(0, 8), 0x99999999L);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(12, 4), 123);

        var response = this.udpTrackerService.HandlePacket(announcePacket, new IPEndPoint(IPAddress.Loopback, 12345));
        response.Should().NotBeNull();
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(3);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(123);
        Encoding.UTF8.GetString(response.AsSpan(8)).Should().Contain("Connection ID expired or invalid");
    }

    [Test]
    public void HandlePacket_Announce_InvalidPacketLength_ReturnsErrorResponse()
    {
        // 1. Connect
        var connectPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), 0x41727101980L);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), 1);
        var connectResp = this.udpTrackerService.HandlePacket(connectPacket, new IPEndPoint(IPAddress.Loopback, 12345));
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResp.AsSpan(8, 8));

        // 2. Send short announce packet (50 bytes instead of 98)
        var announcePacket = new byte[50];
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(12, 4), 777);

        var response = this.udpTrackerService.HandlePacket(announcePacket, new IPEndPoint(IPAddress.Loopback, 12345));
        response.Should().NotBeNull();
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(3);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(777);
        Encoding.UTF8.GetString(response.AsSpan(8)).Should().Contain("Invalid announce packet size");
    }

    [Test]
    public void HandlePacket_Scrape_ValidRequest_ReturnsScrapeResponse()
    {
        // 1. Connect
        var connectPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), 0x41727101980L);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), 1);
        var connectResp = this.udpTrackerService.HandlePacket(connectPacket, new IPEndPoint(IPAddress.Loopback, 12345));
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResp.AsSpan(8, 8));

        // 2. Prepare mock response from trackerService.Scrape
        var hash1 = new byte[20];
        hash1[0] = 1;
        var hash2 = new byte[20];
        hash2[0] = 2;

        this.trackerService.Scrape(Arg.Any<List<byte[]>>()).Returns(new TrackerScrapeResult
        {
            Success = true,
            Files = new List<TrackerScrapeItem>
            {
                new TrackerScrapeItem { InfoHash = hash1, Seeders = 5, Downloaded = 100, Leechers = 2 },
                // hash2 is unregistered, so tracker returns only hash1
            },
        });

        // 3. Scrape packet with 2 hashes (16 header + 2 * 20 = 56 bytes)
        var scrapePacket = new byte[56];
        BinaryPrimitives.WriteInt64BigEndian(scrapePacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(scrapePacket.AsSpan(8, 4), 2); // Action = 2 (Scrape)
        BinaryPrimitives.WriteInt32BigEndian(scrapePacket.AsSpan(12, 4), 888); // TransactionId
        hash1.CopyTo(scrapePacket.AsSpan(16, 20));
        hash2.CopyTo(scrapePacket.AsSpan(36, 20));

        var response = this.udpTrackerService.HandlePacket(scrapePacket, new IPEndPoint(IPAddress.Loopback, 12345));
        response.Should().NotBeNull();
        response.Length.Should().Be(8 + (2 * 12)); // 32 bytes

        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(2); // Action = Scrape
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(888); // TransactionId

        // Hash 1 stats
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(8, 4)).Should().Be(5); // Seeders
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(12, 4)).Should().Be(100); // Downloaded
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(16, 4)).Should().Be(2); // Leechers

        // Hash 2 stats (unregistered => 0, 0, 0)
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(20, 4)).Should().Be(0);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(24, 4)).Should().Be(0);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(28, 4)).Should().Be(0);
    }

    [Test]
    public void HandlePacket_UnknownAction_ReturnsErrorResponse()
    {
        // 1. Connect
        var connectPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), 0x41727101980L);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), 1);
        var connectResp = this.udpTrackerService.HandlePacket(connectPacket, new IPEndPoint(IPAddress.Loopback, 12345));
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResp.AsSpan(8, 8));

        // 2. Action = 99 (unknown)
        var unknownPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(unknownPacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(unknownPacket.AsSpan(8, 4), 99);
        BinaryPrimitives.WriteInt32BigEndian(unknownPacket.AsSpan(12, 4), 555);

        var response = this.udpTrackerService.HandlePacket(unknownPacket, new IPEndPoint(IPAddress.Loopback, 12345));
        response.Should().NotBeNull();
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(3);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(555);
        Encoding.UTF8.GetString(response.AsSpan(8)).Should().Contain("Unknown action");
    }

    [Test]
    public async Task StartAsync_WhenDisabled_DoesNotStart()
    {
        this.configService.TrackerServerEnabled.Returns(false);
        var service = new UdpTrackerService(this.trackerService, this.configService);

        await service.StartAsync();
        service.IsRunning.Should().BeFalse();
    }

    [Test]
    public async Task StartAsync_WhenEnabled_StartsAndStopsSuccessfully()
    {
        this.udpTrackerService.IsRunning.Should().BeFalse();

        await this.udpTrackerService.StartAsync();
        this.udpTrackerService.IsRunning.Should().BeTrue();
        this.udpTrackerService.Port.Should().BeGreaterThan(0);

        await this.udpTrackerService.StopAsync();
        this.udpTrackerService.IsRunning.Should().BeFalse();
    }

    [Test]
    public async Task RestartAsync_RestartsRunningService()
    {
        await this.udpTrackerService.StartAsync();
        this.udpTrackerService.IsRunning.Should().BeTrue();

        await this.udpTrackerService.RestartAsync();
        this.udpTrackerService.IsRunning.Should().BeTrue();

        await this.udpTrackerService.StopAsync();
        this.udpTrackerService.IsRunning.Should().BeFalse();
    }

    [Test]
    public void HandlePacket_Announce_DifferentClientIp_ReturnsErrorResponse()
    {
        // 1. Connect first with 127.0.0.1
        var connectPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), 0x41727101980L);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), 1);

        var connectResp = this.udpTrackerService.HandlePacket(connectPacket, new IPEndPoint(IPAddress.Loopback, 12345));
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResp.AsSpan(8, 8));

        // 2. Announce using the same connectionId from a different IP (10.0.0.1)
        var announcePacket = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(8, 4), 1); // Action = Announce
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(12, 4), 2); // TransactionId = 2

        var spoofedIp = IPAddress.Parse("10.0.0.1");
        var response = this.udpTrackerService.HandlePacket(announcePacket, new IPEndPoint(spoofedIp, 54321));

        response.Should().NotBeNull();
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(3); // Action = Error
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(2); // TransactionId = 2
        Encoding.UTF8.GetString(response.AsSpan(8)).Should().Contain("Connection ID expired or invalid");
    }

    [Test]
    public void HandlePacket_Announce_IPv6Client_Returns18BytePeerRecords()
    {
        var clientIpv6 = IPAddress.Parse("2001:db8::1");
        var peerIpv6 = IPAddress.Parse("2001:db8::2");

        // 1. Connect first with IPv6 endpoint
        var connectPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), 0x41727101980L);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), 1);

        var connectResp = this.udpTrackerService.HandlePacket(connectPacket, new IPEndPoint(clientIpv6, 12345));
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResp.AsSpan(8, 8));

        // 2. Mock trackerService return with IPv6 peer
        this.trackerService.Announce(Arg.Any<TrackerAnnounceRequest>()).Returns(new TrackerAnnounceResult
        {
            Success = true,
            Interval = 1800,
            Leechers = 1,
            Seeders = 2,
            Peers = new List<TrackerPeerState>
            {
                new TrackerPeerState
                {
                    Ip = peerIpv6,
                    Port = 6881,
                    PeerId = new byte[20],
                },
            },
        });

        // 3. Announce request
        var announcePacket = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(8, 4), 1); // Action = Announce
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(12, 4), 42); // TransactionId

        var response = this.udpTrackerService.HandlePacket(announcePacket, new IPEndPoint(clientIpv6, 12345));

        response.Should().NotBeNull();
        // 20 header bytes + 18 peer bytes = 38 bytes total
        response.Length.Should().Be(38);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4)).Should().Be(1); // Action = Announce
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4)).Should().Be(42); // TransactionId
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(8, 4)).Should().Be(1800);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(12, 4)).Should().Be(1);
        BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(16, 4)).Should().Be(2);

        var receivedPeerIp = new IPAddress(response.AsSpan(20, 16));
        receivedPeerIp.Should().Be(peerIpv6);
        BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(36, 2)).Should().Be(6881);
    }
}
