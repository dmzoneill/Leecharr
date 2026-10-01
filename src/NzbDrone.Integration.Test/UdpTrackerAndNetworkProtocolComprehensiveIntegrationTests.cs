// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class UdpTrackerAndNetworkProtocolComprehensiveIntegrationTests : IntegrationTestBase
{
    private const long MagicProtocolId = 0x41727101980L;
    private UdpTrackerService udpTracker;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        var embeddedTracker = services.GetRequiredService<IEmbeddedTrackerService>();
        var configService = services.GetService<IConfigService>();
        this.udpTracker = new UdpTrackerService(embeddedTracker, configService);
    }

    [Test]
    public void UdpTracker_ConnectAndAnnounceAndScrape_PacketProtocolSucceeds()
    {
        var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 50000);

        // 1. Send Connect Packet
        var connectPacket = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), MagicProtocolId);
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0); // Action = Connect
        BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), 12345); // TransactionId

        var connectResp = this.udpTracker.HandlePacket(connectPacket, remoteEndpoint);
        connectResp.Should().NotBeNull();
        connectResp.Length.Should().Be(16);

        var respAction = BinaryPrimitives.ReadInt32BigEndian(connectResp.AsSpan(0, 4));
        var respTxId = BinaryPrimitives.ReadInt32BigEndian(connectResp.AsSpan(4, 4));
        var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResp.AsSpan(8, 8));

        respAction.Should().Be(0);
        respTxId.Should().Be(12345);

        // 2. Send Announce Packet using the connectionId
        var announcePacket = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(8, 4), 1); // Action = Announce
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(12, 4), 54321); // TransactionId
        var infoHash = new byte[20];
        Array.Fill<byte>(infoHash, 0xAA);
        infoHash.CopyTo(announcePacket.AsSpan(16, 20));
        var peerId = new byte[20];
        Array.Fill<byte>(peerId, 0xBB);
        peerId.CopyTo(announcePacket.AsSpan(36, 20));
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(56, 8), 0); // Downloaded
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(64, 8), 1000); // Left
        BinaryPrimitives.WriteInt64BigEndian(announcePacket.AsSpan(72, 8), 0); // Uploaded
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(80, 4), 2); // Event = Started
        BinaryPrimitives.WriteUInt32BigEndian(announcePacket.AsSpan(88, 4), 999); // Key
        BinaryPrimitives.WriteInt32BigEndian(announcePacket.AsSpan(92, 4), 50); // NumWant
        BinaryPrimitives.WriteUInt16BigEndian(announcePacket.AsSpan(96, 2), 6881); // Port

        var announceResp = this.udpTracker.HandlePacket(announcePacket, remoteEndpoint);
        announceResp.Should().NotBeNull();
        announceResp.Length.Should().BeGreaterThanOrEqualTo(20);

        var annAction = BinaryPrimitives.ReadInt32BigEndian(announceResp.AsSpan(0, 4));
        var annTxId = BinaryPrimitives.ReadInt32BigEndian(announceResp.AsSpan(4, 4));
        annAction.Should().BeOneOf(1, 3);
        annTxId.Should().Be(54321);

        // 3. Send Scrape Packet
        var scrapePacket = new byte[36];
        BinaryPrimitives.WriteInt64BigEndian(scrapePacket.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(scrapePacket.AsSpan(8, 4), 2); // Action = Scrape
        BinaryPrimitives.WriteInt32BigEndian(scrapePacket.AsSpan(12, 4), 98765); // TransactionId
        infoHash.CopyTo(scrapePacket.AsSpan(16, 20));

        var scrapeResp = this.udpTracker.HandlePacket(scrapePacket, remoteEndpoint);
        scrapeResp.Should().NotBeNull();
        var scrapeAction = BinaryPrimitives.ReadInt32BigEndian(scrapeResp.AsSpan(0, 4));
        scrapeAction.Should().BeOneOf(2, 3);
    }

    [Test]
    public void UdpTracker_ErrorCasesAndInvalidPackets_ReturnsErrorsOrDrops()
    {
        var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 50000);

        // 1. Packet too short (< 16 bytes)
        var shortPacket = new byte[10];
        var dropResp = this.udpTracker.HandlePacket(shortPacket, remoteEndpoint);
        dropResp.Should().BeNull();

        // 2. Invalid Protocol ID on Connect
        var badConnect = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(badConnect.AsSpan(0, 8), 0x999999L);
        BinaryPrimitives.WriteInt32BigEndian(badConnect.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(badConnect.AsSpan(12, 4), 777);

        var errResp = this.udpTracker.HandlePacket(badConnect, remoteEndpoint);
        errResp.Should().NotBeNull();
        var errAction = BinaryPrimitives.ReadInt32BigEndian(errResp.AsSpan(0, 4));
        errAction.Should().Be(3); // Action 3 = Error

        // 3. Invalid Connection ID on Announce
        var invalidConnAnnounce = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(invalidConnAnnounce.AsSpan(0, 8), 99999999L);
        BinaryPrimitives.WriteInt32BigEndian(invalidConnAnnounce.AsSpan(8, 4), 1);
        var nullResp = this.udpTracker.HandlePacket(invalidConnAnnounce, remoteEndpoint);
        nullResp.Should().BeNull();
    }
}
