// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Leecharr.Api.V1.Peers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Peers;

[TestFixture]
public class PeerConnectionLogControllerTest
{
    private ITorrentService torrentService = null!;
    private IDownloadEngine downloadEngine = null!;
    private IGeoIpService geoIpService = null!;
    private IPeerConnectionHistoryService historyService = null!;
    private PeerConnectionLogController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.geoIpService = Substitute.For<IGeoIpService>();
        this.historyService = new PeerConnectionHistoryService(this.geoIpService);

        this.controller = new PeerConnectionLogController(
            this.torrentService,
            this.downloadEngine,
            this.geoIpService,
            this.historyService);
    }

    [Test]
    public void GetLogs_AppliesStartAndEndAndInfoHashFilters()
    {
        var now = DateTime.UtcNow;
        this.historyService.RecordEvent(new PeerConnectionEvent
        {
            InfoHash = "hash1",
            TorrentName = "Torrent 1",
            RemoteIp = "1.2.3.4",
            RemotePort = 6881,
            PeerId = "client1",
            Timestamp = now.AddHours(-3),
        });

        this.historyService.RecordEvent(new PeerConnectionEvent
        {
            InfoHash = "hash2",
            TorrentName = "Torrent 2",
            RemoteIp = "5.6.7.8",
            RemotePort = 6882,
            PeerId = "client2",
            Timestamp = now.AddHours(-1),
        });

        var result = this.controller.GetLogs(now.AddHours(-2), now, null);
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var logs = (List<PeerConnectionLogResource>)okResult.Value!;
        logs.Should().HaveCount(1);
        logs.First().InfoHash.Should().Be("hash2");
    }

    [Test]
    public void Purge_RemovesEventsBeforeSpecifiedTimestamp()
    {
        var now = DateTime.UtcNow;
        this.historyService.RecordEvent(new PeerConnectionEvent
        {
            InfoHash = "hash1",
            RemoteIp = "1.2.3.4",
            Timestamp = now.AddDays(-5),
        });

        this.historyService.RecordEvent(new PeerConnectionEvent
        {
            InfoHash = "hash2",
            RemoteIp = "5.6.7.8",
            Timestamp = now,
        });

        this.controller.Purge(now.AddDays(-1));

        var remaining = this.historyService.GetRecords();
        remaining.Should().HaveCount(1);
        remaining.First().InfoHash.Should().Be("hash2");
    }

    [Test]
    public void GetActive_ReturnsLiveTaskPeers()
    {
        var torrent = new Torrent { Id = 1, Name = "Active Torrent", InfoHash = "activehash" };
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.GetPeers().Returns(new List<PeerInfo>
        {
            new() { Ip = "9.9.9.9", Port = 51413, Client = "qBittorrent", IsEncrypted = true },
        });

        this.downloadEngine.GetTask(1).Returns(downloadTask);

        var result = this.controller.GetActive();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var logs = (List<PeerConnectionLogResource>)okResult.Value!;
        logs.Should().HaveCount(1);
        logs.First().RemoteIp.Should().Be("9.9.9.9");
        logs.First().InfoHash.Should().Be("activehash");
    }
}
