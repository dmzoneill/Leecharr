// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using MonoTorrent.BEncoding;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class EmbeddedTrackerServiceTest
{
    private IConfigService configService;
    private EmbeddedTrackerService trackerService;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configService.TrackerServerEnabled.Returns(true);
        this.configService.TrackerAnnounceInterval.Returns(1800);
        this.configService.TrackerMaxPeersPerAnnounce.Returns(50);
        this.configService.TrackerPrivateMode.Returns(false);

        this.trackerService = new EmbeddedTrackerService(this.configService);
    }

    [Test]
    public void ProcessAnnounce_CompactMode_ReturnsBEncodedResponseWithCompactPeers()
    {
        var infoHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            infoHash[i] = (byte)(i + 1);
        }

        var request1 = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.100"),
            Port = 6881,
            PeerIdBytes = new byte[20],
            Left = 0, // Seeder
            Compact = true,
        };

        var resp1 = this.trackerService.ProcessAnnounce(request1);
        resp1.Should().NotBeNull();

        var dict1 = (BEncodedDictionary)BEncodedValue.Decode(resp1);
        dict1.ContainsKey("failure reason").Should().BeFalse();
        ((BEncodedNumber)dict1["complete"]).Number.Should().Be(1);
        ((BEncodedNumber)dict1["incomplete"]).Number.Should().Be(0);

        // Second peer announces as leecher
        var request2 = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.101"),
            Port = 6882,
            PeerIdBytes = new byte[20],
            Left = 1000, // Leecher
            Compact = true,
        };

        var resp2 = this.trackerService.ProcessAnnounce(request2);
        var dict2 = (BEncodedDictionary)BEncodedValue.Decode(resp2);
        ((BEncodedNumber)dict2["complete"]).Number.Should().Be(1);
        ((BEncodedNumber)dict2["incomplete"]).Number.Should().Be(1);

        // Verify compact peers returned to peer 2 contains peer 1 (6 bytes: 4 IP + 2 Port)
        var peers = (BEncodedString)dict2["peers"];
        peers.Span.Length.Should().Be(6);
    }

    [Test]
    public void ProcessAnnounce_StoppedEvent_RemovesPeerFromSwarm()
    {
        var infoHash = new byte[20];
        infoHash[0] = 42;

        var startReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.5"),
            Port = 5000,
            Left = 500,
        };
        this.trackerService.ProcessAnnounce(startReq);
        this.trackerService.ActivePeersCount.Should().Be(1);

        var stopReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.5"),
            Port = 5000,
            Event = "stopped",
        };
        this.trackerService.ProcessAnnounce(stopReq);
        this.trackerService.ActivePeersCount.Should().Be(0);
    }

    [Test]
    public void ProcessScrape_ReturnsSwarmStatistics()
    {
        var infoHash = new byte[20];
        infoHash[0] = 99;

        var announceReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 0,
            Event = "completed",
        };
        this.trackerService.ProcessAnnounce(announceReq);

        var scrapeBytes = this.trackerService.ProcessScrape(new List<byte[]> { infoHash });
        var dict = (BEncodedDictionary)BEncodedValue.Decode(scrapeBytes);
        dict.ContainsKey("files").Should().BeTrue();

        var files = (BEncodedDictionary)dict["files"];
        files.Count.Should().Be(1);

        var fileStats = (BEncodedDictionary)files[new BEncodedString(infoHash)];
        ((BEncodedNumber)fileStats["complete"]).Number.Should().Be(1);
        ((BEncodedNumber)fileStats["downloaded"]).Number.Should().Be(1);
    }

    [Test]
    public void ProcessAnnounce_PrivateMode_RejectsUnregisteredSwarm()
    {
        this.configService.TrackerPrivateMode.Returns(true);

        var req = new TrackerAnnounceRequest
        {
            InfoHashBytes = new byte[20],
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
    }
}
