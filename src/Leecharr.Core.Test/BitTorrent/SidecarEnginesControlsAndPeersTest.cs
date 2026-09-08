// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class SidecarEnginesControlsAndPeersTest
{
    private IConfigService configService = null!;
    private IStoragePathService storagePathService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.storagePathService = Substitute.For<IStoragePathService>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
    }

    [Test]
    public void TransmissionDownloadTask_InitializesConnectedPeersToZeroAndSupportsPeerTracking()
    {
        var task = new TransmissionDownloadTask(1, "hash1", "Torrent 1", 1024, "movies");

        task.ConnectedSeeders.Should().Be(0);
        task.ConnectedLeechers.Should().Be(0);
        task.GetPeers().Should().BeEmpty();

        var peers = new List<PeerInfo>
        {
            new()
            {
                Ip = "192.168.1.50",
                Port = 6881,
                Client = "Transmission/4.0.5",
                Flags = "D E",
                Progress = 0.75,
                DownloadSpeed = 102400,
                UploadSpeed = 51200,
                IsEncrypted = true,
                IsUtp = true,
                IsChoked = false,
                IsInterested = true,
                ClientIsChoked = false,
                ClientIsInterested = true,
            },
        };

        task.SetPeers(peers);

        task.GetPeers().Should().HaveCount(1);
        task.GetPeers()[0].Ip.Should().Be("192.168.1.50");
        task.GetPeers()[0].Client.Should().Be("Transmission/4.0.5");
        task.GetPeers()[0].IsEncrypted.Should().BeTrue();
        task.GetPeers()[0].IsUtp.Should().BeTrue();
    }

    [Test]
    public void LibTorrentDownloadTask_InitializesConnectedPeersToZeroAndSupportsPeerTracking()
    {
        var task = new LibTorrentDownloadTask(2, "hash2", "Torrent 2", 2048, "tv");

        task.ConnectedSeeders.Should().Be(0);
        task.ConnectedLeechers.Should().Be(0);
        task.GetPeers().Should().BeEmpty();

        var peers = new List<PeerInfo>
        {
            new()
            {
                Ip = "10.0.0.5",
                Port = 51413,
                Client = "qBittorrent/4.6.0",
                Flags = "uTE",
                Progress = 1.0,
                DownloadSpeed = 500000,
                UploadSpeed = 0,
                IsEncrypted = true,
                IsUtp = true,
                IsChoked = false,
                IsInterested = false,
                ClientIsChoked = true,
                ClientIsInterested = false,
            },
        };

        task.SetPeers(peers);

        task.GetPeers().Should().HaveCount(1);
        task.GetPeers()[0].Ip.Should().Be("10.0.0.5");
        task.GetPeers()[0].Client.Should().Be("qBittorrent/4.6.0");
        task.GetPeers()[0].IsEncrypted.Should().BeTrue();
    }

    [Test]
    public async Task EmbeddedTransmissionEngine_RemoveTrackersAndSetPriority_DoesNotThrowWhenDisconnected()
    {
        using var engine = new EmbeddedTransmissionEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        var torrent = new Torrent { Id = 10, InfoHash = "AABB112233", Name = "Test Torrent", TotalSize = 5000 };
        await engine.AddTorrentAsync(torrent);

        var actRemoveTrackers = async () => await engine.RemoveTrackersAsync(10, new[] { "http://tracker.example.com/announce" });
        await actRemoveTrackers.Should().NotThrowAsync();

        var actSetPriority = async () => await engine.SetFilePriorityAsync(10, "test.mkv", 4);
        await actSetPriority.Should().NotThrowAsync();
    }

    [Test]
    public async Task LibTorrentDownloadEngine_RemoveTrackersAndSetPriority_DoesNotThrowWhenDisconnected()
    {
        using var engine = new LibTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        var torrent = new Torrent { Id = 20, InfoHash = "CCDDEE4455", Name = "Test Torrent 2", TotalSize = 10000 };
        await engine.AddTorrentAsync(torrent);

        var actRemoveTrackers = async () => await engine.RemoveTrackersAsync(20, new[] { "http://tracker.example.com/announce" });
        await actRemoveTrackers.Should().NotThrowAsync();

        var actSetPriority = async () => await engine.SetFilePriorityAsync(20, "video.mp4", 0);
        await actSetPriority.Should().NotThrowAsync();
    }
}
