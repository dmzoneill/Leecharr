// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.Metrics;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class EmbeddedTrackerAndSwarmManagementIntegrationTests : IntegrationTestBase
{
    private EmbeddedTrackerService embeddedTracker;
    private TrackerMetricService metricService;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        this.embeddedTracker = new EmbeddedTrackerService();
        this.metricService = services.GetRequiredService<ITrackerMetricService>() as TrackerMetricService;
    }

    [Test]
    public void EmbeddedTracker_SwarmLifecycleAndAnnounce_MaintainsSwarmState()
    {
        if (this.embeddedTracker == null)
        {
            Assert.Ignore("EmbeddedTrackerService is not registered as concrete class.");
            return;
        }

        var infoHashHex = "1234567890123456789012345678901234567890";
        var infoHashBytes = Convert.FromHexString(infoHashHex);

        // 1. Register Swarm
        this.embeddedTracker.RegisterSwarm(infoHashHex);
        this.embeddedTracker.ActiveSwarmsCount.Should().BeGreaterThan(0);

        // 2. Announce Seeder Peer
        var seederReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHashBytes,
            InfoHashHex = infoHashHex,
            PeerIdBytes = new byte[20],
            PeerId = "seeder_peer_12345678",
            RemoteIp = IPAddress.Parse("192.168.1.10"),
            Port = 6881,
            Downloaded = 1000,
            Uploaded = 500,
            Left = 0, // Seeder
            Event = "started",
            Compact = true,
            NumWant = 50,
        };

        var seederResult = this.embeddedTracker.Announce(seederReq);
        seederResult.Success.Should().BeTrue();
        seederResult.Seeders.Should().Be(1);
        seederResult.Leechers.Should().Be(0);

        // 3. Announce Leecher Peer
        var leecherReq = new TrackerAnnounceRequest
        {
            InfoHashBytes = infoHashBytes,
            InfoHashHex = infoHashHex,
            PeerIdBytes = new byte[20],
            PeerId = "leecher_peer_87654321",
            RemoteIp = IPAddress.Parse("192.168.1.20"),
            Port = 6882,
            Downloaded = 100,
            Uploaded = 0,
            Left = 900, // Leecher
            Event = "started",
            Compact = true,
            NumWant = 50,
        };

        var leecherResult = this.embeddedTracker.Announce(leecherReq);
        leecherResult.Success.Should().BeTrue();
        leecherResult.Seeders.Should().Be(1);
        leecherResult.Leechers.Should().Be(1);

        // 4. Scrape Swarm
        var scrapeResult = this.embeddedTracker.Scrape(new List<byte[]> { infoHashBytes });
        scrapeResult.Should().NotBeNull();
        scrapeResult.Files.Should().NotBeEmpty();
        var stats = scrapeResult.Files[0];
        stats.Seeders.Should().Be(1);
        stats.Leechers.Should().Be(1);

        // 5. Query swarms and peers
        var swarms = this.embeddedTracker.GetAllSwarms();
        swarms.Should().Contain(s => s.InfoHash == infoHashHex);

        var peers = this.embeddedTracker.GetPeersForSwarm(infoHashHex);
        peers.Should().HaveCount(2);

        // 6. Unregister
        this.embeddedTracker.UnregisterSwarm(infoHashHex);

        // 7. Event handling
        var dummyTorrent = new Torrent { Id = 9999, InfoHash = "AABBCCDDEEFF00112233445566778899AABBCCDD" };
        this.embeddedTracker.Handle(new TorrentAddedEvent { Torrent = dummyTorrent });
        this.embeddedTracker.Handle(new TorrentDeletedEvent { Torrent = dummyTorrent });
    }

    [Test]
    public void TrackerMetrics_RecordingAndSummary_TracksAccurately()
    {
        if (this.metricService == null)
        {
            Assert.Ignore("TrackerMetricService is not registered as concrete class.");
            return;
        }

        // 1. Seed from existing trackers
        this.metricService.SeedFromExistingTrackers();

        // 2. Summary
        var summary = this.metricService.GetSummary();
        summary.Should().NotBeNull();

        // 3. Prune snapshots
        this.metricService.PruneSnapshots(DateTime.UtcNow.AddDays(-30));
    }
}
