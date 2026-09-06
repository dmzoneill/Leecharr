// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using FluentAssertions;
using MonoTorrent.BEncoding;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class EmbeddedTrackerServiceTest
{
    private IConfigService configService;
    private ITorrentRepository torrentRepository;
    private EmbeddedTrackerService trackerService;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configService.TrackerServerEnabled.Returns(true);
        this.configService.TrackerEnableScrape.Returns(true);
        this.configService.TrackerAnnounceInterval.Returns(1800);
        this.configService.TrackerMaxPeersPerAnnounce.Returns(50);
        this.configService.TrackerEnableScrape.Returns(true);
        this.torrentRepository = Substitute.For<ITorrentRepository>();

        this.trackerService = new EmbeddedTrackerService(this.configService, this.torrentRepository);
    }

    [TearDown]
    public void TearDown()
    {
        this.trackerService?.Dispose();
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
    public void ProcessAnnounce_StoppedEvent_RemovesPeerAndPrunesEmptySwarm()
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
        this.trackerService.ActiveSwarmsCount.Should().Be(1);

        var stopReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.5"),
            Port = 5000,
            Event = "stopped",
        };
        this.trackerService.ProcessAnnounce(stopReq);
        this.trackerService.ActivePeersCount.Should().Be(0);
        this.trackerService.ActiveSwarmsCount.Should().Be(0);
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

        var infoHash = new byte[20];
        infoHash[0] = 1;

        var req = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Be("Torrent not registered on this private tracker.");
    }

    [Test]
    public void ProcessAnnounce_InvalidInfoHash_MissingHash_ReturnsFailure()
    {
        var req = new TrackerAnnounceRequest
        {
            InfoHashBytes = null,
            InfoHashHex = null,
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Contain("Missing info_hash");
    }

    [TestCase(0)]
    [TestCase(10)]
    [TestCase(19)]
    [TestCase(21)]
    [TestCase(32)]
    public void ProcessAnnounce_InvalidInfoHash_InvalidByteLength_ReturnsFailure(int byteLength)
    {
        var infoHash = new byte[byteLength];
        if (byteLength > 0)
        {
            infoHash[0] = 0xAA;
        }

        var req = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Contain("must be exactly 20 bytes");
    }

    [TestCase("1234")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 38 chars
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 42 chars
    public void ProcessAnnounce_InvalidInfoHash_InvalidHexLength_ReturnsFailure(string invalidHex)
    {
        var req = new TrackerAnnounceRequest
        {
            InfoHashHex = invalidHex,
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Contain("must be exactly 40 characters");
    }

    [Test]
    public void ProcessAnnounce_InvalidInfoHash_MalformedHexCharacters_ReturnsFailure()
    {
        // 40 characters containing invalid non-hex characters 'Z'
        var invalidHex = new string('Z', 40);

        var req = new TrackerAnnounceRequest
        {
            InfoHashHex = invalidHex,
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Contain("malformed hex");
    }

    [Test]
    public void ProcessAnnounce_InvalidInfoHash_AllZeroBytes_ReturnsFailure()
    {
        var req = new TrackerAnnounceRequest
        {
            InfoHashBytes = new byte[20], // 20 null bytes
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Contain("null hash");
    }

    [Test]
    public void ProcessAnnounce_InvalidInfoHash_AllZeroHex_ReturnsFailure()
    {
        var req = new TrackerAnnounceRequest
        {
            InfoHashHex = new string('0', 40),
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Contain("null hash");
    }

    [Test]
    public void ProcessAnnounce_InvalidInfoHash_MismatchedBytesAndHex_ReturnsFailure()
    {
        var hash1 = new byte[20];
        hash1[0] = 1;
        var hash2 = new byte[20];
        hash2[0] = 2;

        var req = new TrackerAnnounceRequest
        {
            InfoHashBytes = hash1,
            InfoHashHex = Convert.ToHexString(hash2),
            RemoteIp = IPAddress.Loopback,
            Port = 6881,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Contain("do not match");
    }

    [Test]
    public void PruneInactivePeers_RemovesExpiredPeersAndEmptySwarms()
    {
        var hash1 = new byte[20];
        hash1[0] = 1;

        var req = new TrackerAnnounceRequest
        {
            InfoHashBytes = hash1,
            RemoteIp = IPAddress.Parse("10.10.10.10"),
            Port = 6881,
            Left = 100,
        };

        this.trackerService.ProcessAnnounce(req);
        this.trackerService.ActiveSwarmsCount.Should().Be(1);
        this.trackerService.ActivePeersCount.Should().Be(1);

        // Pruning with zero timeout forces immediate expiration of all peers
        this.trackerService.PruneInactivePeers(TimeSpan.Zero);

        this.trackerService.ActivePeersCount.Should().Be(0);
        this.trackerService.ActiveSwarmsCount.Should().Be(0);
    }

    [Test]
    public void ProcessAnnounce_SwarmLimitReached_PrunesInactivePeersFromUnregisteredSwarms()
    {
        this.configService.TrackerAnnounceInterval.Returns(1);
        this.trackerService.MaxSwarms = 2;

        var hex1 = "1111111111111111111111111111111111111111";
        var hex2 = "2222222222222222222222222222222222222222";
        var hex3 = "3333333333333333333333333333333333333333";

        // Peer announces to swarm 1 first
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashHex = hex1,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 100,
        });

        // Peer announces to swarm 2 second
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashHex = hex2,
            RemoteIp = IPAddress.Parse("10.0.0.2"),
            Port = 6882,
            Left = 100,
        });

        this.trackerService.ActiveSwarmsCount.Should().Be(2);

        // Wait for peer in swarm 1 & 2 to exceed 2x interval (2 seconds)
        Thread.Sleep(2100);

        // Now announce to swarm 3 (new swarm). Capacity is full (2/2), but inactive peers in unregistered swarms are pruned.
        var req = new TrackerAnnounceRequest
        {
            InfoHashHex = hex3,
            RemoteIp = IPAddress.Parse("10.0.0.3"),
            Port = 6883,
            Left = 0,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeFalse();

        this.trackerService.ActiveSwarmsCount.Should().BeGreaterThan(0);
    }

    [Test]
    public void ProcessAnnounce_SwarmLimitReached_RegisteredEmptySwarms_NotEvicted_RejectsNewSwarm()
    {
        this.trackerService.MaxSwarms = 2;

        var hex1 = "1111111111111111111111111111111111111111";
        var hex2 = "2222222222222222222222222222222222222222";
        var hex3 = "3333333333333333333333333333333333333333";

        // Register swarm 1 and 2 (both empty, registered internally by Leecharr)
        this.trackerService.RegisterSwarm(hex1);
        this.trackerService.RegisterSwarm(hex2);

        this.trackerService.ActiveSwarmsCount.Should().Be(2);
        this.trackerService.ActivePeersCount.Should().Be(0);

        // Try announcing to a 3rd swarm when capacity is full of registered swarms.
        // Registered swarms must NOT be evicted.
        var req = new TrackerAnnounceRequest
        {
            InfoHashHex = hex3,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 0,
        };

        var bytes = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(bytes);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Be("Tracker swarm limit reached.");

        // Both registered swarms remain intact
        this.trackerService.ActiveSwarmsCount.Should().Be(2);
    }

    [Test]
    public void ProcessAnnounce_SwarmLimitReached_AllSwarmsActive_RejectsNewSwarm()
    {
        this.trackerService.MaxSwarms = 2;

        var hash1 = new byte[20];
        hash1[0] = 1;
        var hash2 = new byte[20];
        hash2[0] = 2;
        var hash3 = new byte[20];
        hash3[0] = 3;

        // Swarm 1 has active peer
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = hash1,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 100,
        });

        // Swarm 2 has active peer
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = hash2,
            RemoteIp = IPAddress.Parse("10.0.0.2"),
            Port = 6882,
            Left = 100,
        });

        this.trackerService.ActiveSwarmsCount.Should().Be(2);
        this.trackerService.ActivePeersCount.Should().Be(2);

        // Attempting to announce to a 3rd swarm when capacity is 2 and all are active
        var resp = this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = hash3,
            RemoteIp = IPAddress.Parse("10.0.0.3"),
            Port = 6883,
            Left = 100,
        });

        var dict = (BEncodedDictionary)BEncodedValue.Decode(resp);
        dict.ContainsKey("failure reason").Should().BeTrue();
        ((BEncodedString)dict["failure reason"]).Text.Should().Be("Tracker swarm limit reached.");

        this.trackerService.ActiveSwarmsCount.Should().Be(2);
    }

    [Test]
    public void RegisterSwarm_RejectsMalformedAndNullHashes()
    {
        this.trackerService.RegisterSwarm(null);
        this.trackerService.RegisterSwarm(string.Empty);
        this.trackerService.RegisterSwarm("not_a_hex");
        this.trackerService.RegisterSwarm(new string('0', 40)); // null hash
        this.trackerService.RegisterSwarm(new string('A', 38)); // too short

        this.trackerService.ActiveSwarmsCount.Should().Be(0);

        // Valid hash works
        this.trackerService.RegisterSwarm("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        this.trackerService.ActiveSwarmsCount.Should().Be(1);
    }

    [Test]
    public void ProcessAnnounce_CompactMode_WithIPv6Peers_SerializesPeers6KeyUnderBep7()
    {
        var infoHash = new byte[20];
        infoHash[0] = 0xAA;

        // Peer 1: IPv6
        var ipv6Address = IPAddress.Parse("2001:db8::1");
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = ipv6Address,
            Port = 6881,
            Left = 0,
            Compact = true,
        });

        // Peer 2: IPv4
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.100"),
            Port = 6882,
            Left = 100,
            Compact = true,
        });

        // Peer 3 announces and requests compact peers
        var resp = this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 5000,
            Left = 100,
            Compact = true,
        });

        var dict = (BEncodedDictionary)BEncodedValue.Decode(resp);
        dict.ContainsKey("peers").Should().BeTrue();
        dict.ContainsKey("peers6").Should().BeTrue();

        // IPv4 peer packed into peers (6 bytes)
        var peers4 = ((BEncodedString)dict["peers"]).Span;
        peers4.Length.Should().Be(6);
        peers4[0].Should().Be(192);
        peers4[1].Should().Be(168);
        peers4[2].Should().Be(1);
        peers4[3].Should().Be(100);
        BinaryPrimitives.ReadUInt16BigEndian(peers4.Slice(4, 2)).Should().Be(6882);

        // IPv6 peer packed into peers6 (18 bytes) per BEP 7
        var peers6 = ((BEncodedString)dict["peers6"]).Span;
        peers6.Length.Should().Be(18);
        peers6.Slice(0, 16).ToArray().Should().Equal(ipv6Address.GetAddressBytes());
        BinaryPrimitives.ReadUInt16BigEndian(peers6.Slice(16, 2)).Should().Be(6881);
    }

    [Test]
    public void ProcessAnnounce_CompactMode_WithIPv4MappedIPv6Address_MapsToIPv4AndPacksIntoPeers()
    {
        var infoHash = new byte[20];
        infoHash[0] = 0xBB;

        // Peer with IPv4-mapped IPv6 address (e.g. ::ffff:192.0.2.1)
        var mappedIp = IPAddress.Parse("::ffff:192.0.2.1");
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = mappedIp,
            Port = 6881,
            Left = 0,
            Compact = true,
        });

        // Announce from another peer
        var resp = this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 5000,
            Left = 100,
            Compact = true,
        });

        var dict = (BEncodedDictionary)BEncodedValue.Decode(resp);
        dict.ContainsKey("peers").Should().BeTrue();
        dict.ContainsKey("peers6").Should().BeFalse();

        var peers4 = ((BEncodedString)dict["peers"]).Span;
        peers4.Length.Should().Be(6);
        peers4[0].Should().Be(192);
        peers4[1].Should().Be(0);
        peers4[2].Should().Be(2);
        peers4[3].Should().Be(1);
        BinaryPrimitives.ReadUInt16BigEndian(peers4.Slice(4, 2)).Should().Be(6881);
    }

    [Test]
    public void ProcessAnnounce_NonCompactMode_ReturnsBothIPv4AndIPv6InPeersList()
    {
        var infoHash = new byte[20];
        infoHash[0] = 0xCC;

        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.1"),
            Port = 6881,
            Left = 0,
            PeerIdBytes = Encoding.ASCII.GetBytes("-TR3000-012345678901"),
            Compact = false,
        });

        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("2001:db8::1"),
            Port = 6882,
            Left = 50,
            PeerIdBytes = Encoding.ASCII.GetBytes("-TR3000-012345678902"),
            Compact = false,
        });

        var resp = this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 5000,
            Left = 100,
            Compact = false,
        });

        var dict = (BEncodedDictionary)BEncodedValue.Decode(resp);
        dict.ContainsKey("peers").Should().BeTrue();
        var list = (BEncodedList)dict["peers"];
        list.Count.Should().Be(2);

        var ips = list.Cast<BEncodedDictionary>().Select(d => ((BEncodedString)d["ip"]).Text).ToList();
        ips.Should().Contain("192.168.1.1");
        ips.Should().Contain("2001:db8::1");
    }

    [Test]
    public void ProcessAnnounce_CandidatePeers_AreShuffledRandomly()
    {
        var infoHash = new byte[20];
        infoHash[0] = 0xDD;

        // Register 20 peers with distinct ports
        for (var i = 1; i <= 20; i++)
        {
            this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
            {
                InfoHashBytes = infoHash,
                RemoteIp = IPAddress.Parse($"10.0.0.{i}"),
                Port = 6000 + i,
                Left = 100,
                Compact = false,
            });
        }

        // Query multiple times requesting 10 peers each time
        var orderSignatures = new HashSet<string>();
        for (var trial = 0; trial < 10; trial++)
        {
            var resp = this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
            {
                InfoHashBytes = infoHash,
                RemoteIp = IPAddress.Parse("172.16.0.1"),
                Port = 9999,
                NumWant = 10,
                Compact = false,
            });

            var dict = (BEncodedDictionary)BEncodedValue.Decode(resp);
            var list = (BEncodedList)dict["peers"];
            list.Count.Should().Be(10);

            var ports = string.Join(",", list.Cast<BEncodedDictionary>().Select(d => ((BEncodedNumber)d["port"]).Number));
            orderSignatures.Add(ports);
        }

        // Over 10 random trials of 20 peers, we should see multiple distinct orderings (not static dictionary order)
        orderSignatures.Count.Should().BeGreaterThan(1);
    }

    [Test]
    public void ProcessScrape_BinaryInfoHash_AndHexInfoHash_ReturnsProperlyEncodedScrapeMetrics()
    {
        var infoHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            infoHash[i] = (byte)(i + 1);
        }

        // Add 2 seeders and 1 leecher
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 0,
        });
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.2"),
            Port = 6882,
            Left = 0,
        });
        this.trackerService.ProcessAnnounce(new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("10.0.0.3"),
            Port = 6883,
            Left = 500,
        });

        // 1. Scrape using binary info_hash (20 bytes)
        var binaryScrapeResp = this.trackerService.ProcessScrape(new List<byte[]> { infoHash });
        var binaryDict = (BEncodedDictionary)BEncodedValue.Decode(binaryScrapeResp);
        binaryDict.ContainsKey("files").Should().BeTrue();
        var files = (BEncodedDictionary)binaryDict["files"];
        files.ContainsKey(new BEncodedString(infoHash)).Should().BeTrue();

        var metrics = (BEncodedDictionary)files[new BEncodedString(infoHash)];
        ((BEncodedNumber)metrics["complete"]).Number.Should().Be(2);
        ((BEncodedNumber)metrics["incomplete"]).Number.Should().Be(1);
        ((BEncodedNumber)metrics["downloaded"]).Number.Should().Be(0);

        // 2. Scrape using 40-char ASCII hex representation of info_hash
        var hexStr = Convert.ToHexString(infoHash);
        var hexBytes = Encoding.ASCII.GetBytes(hexStr);
        var hexScrapeResp = this.trackerService.ProcessScrape(new List<byte[]> { hexBytes });
        var hexDict = (BEncodedDictionary)BEncodedValue.Decode(hexScrapeResp);
        var hexFiles = (BEncodedDictionary)hexDict["files"];
        hexFiles.ContainsKey(new BEncodedString(infoHash)).Should().BeTrue();

        // 3. Scrape all swarms (null list)
        var scrapeAllResp = this.trackerService.ProcessScrape(null);
        var allDict = (BEncodedDictionary)BEncodedValue.Decode(scrapeAllResp);
        var allFiles = (BEncodedDictionary)allDict["files"];
        allFiles.ContainsKey(new BEncodedString(infoHash)).Should().BeTrue();
    }

    [Test]
    public void RegisterSwarm_WhenSwarmAlreadyExists_SetsIsRegisteredTrue_WithoutConsumingExtraSlot()
    {
        this.trackerService.MaxSwarms = 1;
        var hex = "1111111111111111111111111111111111111111";

        // Swarm created via public announce
        var req = new TrackerAnnounceRequest
        {
            InfoHashHex = hex,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 100,
        };
        this.trackerService.ProcessAnnounce(req);
        this.trackerService.ActiveSwarmsCount.Should().Be(1);

        // Registering the existing swarm marks it registered and succeeds even at MaxSwarms capacity
        this.trackerService.RegisterSwarm(hex);
        this.trackerService.ActiveSwarmsCount.Should().Be(1);

        // Switch to private mode; announce must succeed because it is now registered
        this.configService.TrackerPrivateMode.Returns(true);
        var resp = this.trackerService.ProcessAnnounce(req);
        var dict = (BEncodedDictionary)BEncodedValue.Decode(resp);
        dict.ContainsKey("failure reason").Should().BeFalse();
    }

    [Test]
    public void Handle_TorrentAddedAndDeletedEvents_RegistersAndUnregistersSwarm()
    {
        this.configService.TrackerPrivateMode.Returns(true);
        var hex = "1111111111111111111111111111111111111111";
        var torrent = new Torrent { InfoHash = hex };

        // Before adding, private announce fails
        var req = new TrackerAnnounceRequest
        {
            InfoHashHex = hex,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 0,
        };
        var resp1 = this.trackerService.ProcessAnnounce(req);
        ((BEncodedDictionary)BEncodedValue.Decode(resp1)).ContainsKey("failure reason").Should().BeTrue();

        // Handle TorrentAddedEvent
        this.trackerService.Handle(new TorrentAddedEvent { Torrent = torrent });

        // Now private announce succeeds
        var resp2 = this.trackerService.ProcessAnnounce(req);
        ((BEncodedDictionary)BEncodedValue.Decode(resp2)).ContainsKey("failure reason").Should().BeFalse();

        // Handle TorrentDeletedEvent
        this.trackerService.Handle(new TorrentDeletedEvent { Torrent = torrent });

        // Swarm is unregistered; another announce in private mode fails
        var reqLeecher = new TrackerAnnounceRequest
        {
            InfoHashHex = hex,
            RemoteIp = IPAddress.Parse("10.0.0.2"),
            Port = 6882,
            Left = 100,
        };
        var resp3 = this.trackerService.ProcessAnnounce(reqLeecher);
        ((BEncodedDictionary)BEncodedValue.Decode(resp3)).ContainsKey("failure reason").Should().BeTrue();
    }

    [Test]
    public void Handle_ApplicationStartedEvent_RegistersAllExistingTorrents()
    {
        this.configService.TrackerPrivateMode.Returns(true);
        var hex1 = "1111111111111111111111111111111111111111";
        var hex2 = "2222222222222222222222222222222222222222";

        this.torrentRepository.All().Returns(new List<Torrent>
        {
            new Torrent { InfoHash = hex1 },
            new Torrent { InfoHash = hex2 },
        });

        this.trackerService.Handle(new ApplicationStartedEvent());

        this.trackerService.ActiveSwarmsCount.Should().Be(2);

        var req1 = new TrackerAnnounceRequest
        {
            InfoHashHex = hex1,
            RemoteIp = IPAddress.Parse("10.0.0.1"),
            Port = 6881,
            Left = 0,
        };
        var resp1 = this.trackerService.ProcessAnnounce(req1);
        ((BEncodedDictionary)BEncodedValue.Decode(resp1)).ContainsKey("failure reason").Should().BeFalse();

        var req2 = new TrackerAnnounceRequest
        {
            InfoHashHex = hex2,
            RemoteIp = IPAddress.Parse("10.0.0.2"),
            Port = 6882,
            Left = 0,
        };
        var resp2 = this.trackerService.ProcessAnnounce(req2);
        ((BEncodedDictionary)BEncodedValue.Decode(resp2)).ContainsKey("failure reason").Should().BeFalse();
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(99999)]
    public void Announce_InvalidPort_ReturnsFailure(int invalidPort)
    {
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)1);

        var request = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.100"),
            Port = invalidPort,
            PeerIdBytes = new byte[20],
            Left = 1000,
        };

        var result = this.trackerService.Announce(request);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Invalid port");
    }

    [Test]
    public void Announce_CompletedEvent_OnlyIncrementsDownloadedCountOncePerPeer()
    {
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)2);

        // 1. Initial leecher announce
        var leecherReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.50"),
            Port = 6881,
            PeerIdBytes = new byte[20],
            Left = 5000,
        };
        this.trackerService.Announce(leecherReq);

        // 2. Leecher completes download
        var completedReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.50"),
            Port = 6881,
            PeerIdBytes = new byte[20],
            Event = "completed",
            Left = 0,
        };
        var res1 = this.trackerService.Announce(completedReq);
        res1.Success.Should().BeTrue();

        var scrape1 = this.trackerService.Scrape(new List<byte[]> { infoHash });
        scrape1.Should().NotBeNull();
        scrape1.Files.Should().NotBeEmpty();
        scrape1.Files[0].Downloaded.Should().Be(1);

        // 3. Re-announcing completed should NOT increment downloaded count again
        var reannounceCompletedReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHash,
            RemoteIp = IPAddress.Parse("192.168.1.50"),
            Port = 6881,
            PeerIdBytes = new byte[20],
            Event = "completed",
            Left = 0,
        };
        var res2 = this.trackerService.Announce(reannounceCompletedReq);
        res2.Success.Should().BeTrue();

        var scrape2 = this.trackerService.Scrape(new List<byte[]> { infoHash });
        scrape2.Files[0].Downloaded.Should().Be(1);
    }
}
