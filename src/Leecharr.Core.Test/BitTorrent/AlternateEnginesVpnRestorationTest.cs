// Copyright (c) PlaceholderCompany. All rights reserved.

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
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class AlternateEnginesVpnRestorationTest
{
    private IConfigService configService;
    private IStoragePathService storagePathService;
    private ICategoryService categoryService;
    private IDiskProvider diskProvider;
    private IEventAggregator eventAggregator;

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
    public async Task EmbeddedTransmissionEngine_ShouldResumeCompletedTorrentsAsSeeding()
    {
        var engine = new EmbeddedTransmissionEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        await engine.StartAsync();

        var completedTorrent = new Torrent { Id = 1, InfoHash = "AABBCC11", Name = "Completed Torrent", TotalSize = 1000 };
        var incompleteTorrent = new Torrent { Id = 2, InfoHash = "AABBCC22", Name = "Incomplete Torrent", TotalSize = 1000 };

        var task1 = (TransmissionDownloadTask)await engine.AddTorrentAsync(completedTorrent);
        var task2 = (TransmissionDownloadTask)await engine.AddTorrentAsync(incompleteTorrent);

        task1.Progress = 1.0;
        task1.Status = TorrentStatus.Seeding;

        task2.Progress = 0.5;
        task2.Status = TorrentStatus.Downloading;

        // Trigger VPN kill switch
        engine.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

        task1.Status.Should().Be(TorrentStatus.Paused);
        task2.Status.Should().Be(TorrentStatus.Paused);

        // Trigger VPN interface restored
        engine.Handle(new VpnInterfaceRestoredEvent("tun0"));

        task1.Status.Should().Be(TorrentStatus.Seeding);
        task2.Status.Should().Be(TorrentStatus.Downloading);
    }

    [Test]
    public async Task LibTorrentDownloadEngine_ShouldResumeCompletedTorrentsAsSeeding()
    {
        var engine = new LibTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);

        await engine.StartAsync();

        var completedTorrent = new Torrent { Id = 1, InfoHash = "AABBCC11", Name = "Completed Torrent", TotalSize = 1000 };
        var incompleteTorrent = new Torrent { Id = 2, InfoHash = "AABBCC22", Name = "Incomplete Torrent", TotalSize = 1000 };

        var task1 = (LibTorrentDownloadTask)await engine.AddTorrentAsync(completedTorrent);
        var task2 = (LibTorrentDownloadTask)await engine.AddTorrentAsync(incompleteTorrent);

        task1.Progress = 1.0;
        task1.Status = TorrentStatus.Seeding;

        task2.Progress = 0.5;
        task2.Status = TorrentStatus.Downloading;

        // Trigger VPN kill switch
        engine.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

        task1.Status.Should().Be(TorrentStatus.Paused);
        task2.Status.Should().Be(TorrentStatus.Paused);

        // Trigger VPN interface restored
        engine.Handle(new VpnInterfaceRestoredEvent("tun0"));

        task1.Status.Should().Be(TorrentStatus.Seeding);
        task2.Status.Should().Be(TorrentStatus.Downloading);
    }
}
