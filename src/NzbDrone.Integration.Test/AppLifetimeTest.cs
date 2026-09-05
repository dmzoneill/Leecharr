// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Authentication;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.SystemServices;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;
using NzbDrone.Host;
using NzbDrone.SignalR;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class AppLifetimeTest
{
    private IConfigService configService;
    private IEventAggregator eventAggregator;
    private IDownloadEngine downloadEngine;
    private ITorrentRepository torrentRepository;
    private IWatchFolderService watchFolderService;
    private INetworkSecurityService networkSecurityService;
    private IRssSyncService rssSyncService;
    private IDynamicAuthSchemeManager dynamicAuthManager;
    private ITorrentService torrentService;
    private IPowerManagementService powerManagementService;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.watchFolderService = Substitute.For<IWatchFolderService>();
        this.networkSecurityService = Substitute.For<INetworkSecurityService>();
        this.rssSyncService = Substitute.For<IRssSyncService>();
        this.dynamicAuthManager = Substitute.For<IDynamicAuthSchemeManager>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.powerManagementService = Substitute.For<IPowerManagementService>();
    }

    [Test]
    public async Task AutoShutdown_WhenDownloadsComplete_DoesNotTriggerOnStartupWithStaleCompletedTorrents()
    {
        this.configService.AutoShutdownAction.Returns("Shutdown");
        this.configService.AutoShutdownCondition.Returns("WhenDownloadsComplete");
        this.configService.WatchFolderScanIntervalSeconds.Returns(1000);

        var completedTorrent = new Torrent
        {
            Id = 1,
            Name = "Existing Completed",
            Status = TorrentStatus.Stopped,
            Progress = 1.0,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { completedTorrent });
        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask>());

        using var lifetime = new AppLifetime(
            this.configService,
            this.eventAggregator,
            this.downloadEngine,
            this.torrentRepository,
            this.watchFolderService,
            this.networkSecurityService,
            this.rssSyncService,
            this.dynamicAuthManager,
            this.torrentService,
            powerManagementService: this.powerManagementService);

        await lifetime.StartAsync(CancellationToken.None);

        // Wait past 5 maintenance ticks (1 sec each in loop)
        await Task.Delay(6000);
        await lifetime.StopAsync(CancellationToken.None);

        await this.powerManagementService.DidNotReceiveWithAnyArgs().ExecutePowerActionAsync(default);
        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => d.ContainsKey("AutoShutdownAction")));
    }

    [Test]
    public async Task AutoShutdown_WhenDownloadsComplete_TriggersAndResetsWhenActiveDownloadCompletes()
    {
        this.configService.AutoShutdownAction.Returns("Shutdown");
        this.configService.AutoShutdownCondition.Returns("WhenDownloadsComplete");
        this.configService.WatchFolderScanIntervalSeconds.Returns(1000);

        var downloadingTorrent = new Torrent
        {
            Id = 1,
            Name = "Active Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { downloadingTorrent });

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.Status.Returns(TorrentStatus.Downloading);
        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { mockTask });

        using var lifetime = new AppLifetime(
            this.configService,
            this.eventAggregator,
            this.downloadEngine,
            this.torrentRepository,
            this.watchFolderService,
            this.networkSecurityService,
            this.rssSyncService,
            this.dynamicAuthManager,
            this.torrentService,
            powerManagementService: this.powerManagementService);

        await lifetime.StartAsync(CancellationToken.None);

        // Wait for download session state to latch in 1s loop
        await Task.Delay(1500);

        // Transition download to completed
        mockTask.Status.Returns(TorrentStatus.Stopped);
        downloadingTorrent.Status = TorrentStatus.Stopped;
        downloadingTorrent.Progress = 1.0;

        // Wait past 5s maintenance tick
        await Task.Delay(6000);
        await lifetime.StopAsync(CancellationToken.None);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            d.ContainsKey("AutoShutdownAction") && (string)d["AutoShutdownAction"] == "None"));
        await this.powerManagementService.Received(1).ExecutePowerActionAsync(PowerAction.Shutdown);
    }

    [Test]
    public async Task AutoShutdown_WhenAllTorrentsComplete_DoesNotTriggerOnStartupWithCompletedTorrents()
    {
        this.configService.AutoShutdownAction.Returns("Shutdown");
        this.configService.AutoShutdownCondition.Returns("WhenAllTorrentsComplete");
        this.configService.WatchFolderScanIntervalSeconds.Returns(1000);

        var completedTorrent = new Torrent
        {
            Id = 1,
            Name = "Existing Completed",
            Status = TorrentStatus.Stopped,
            Progress = 1.0,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { completedTorrent });
        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask>());

        using var lifetime = new AppLifetime(
            this.configService,
            this.eventAggregator,
            this.downloadEngine,
            this.torrentRepository,
            this.watchFolderService,
            this.networkSecurityService,
            this.rssSyncService,
            this.dynamicAuthManager,
            this.torrentService,
            powerManagementService: this.powerManagementService);

        await lifetime.StartAsync(CancellationToken.None);

        await Task.Delay(6000);
        await lifetime.StopAsync(CancellationToken.None);

        await this.powerManagementService.DidNotReceiveWithAnyArgs().ExecutePowerActionAsync(default);
        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => d.ContainsKey("AutoShutdownAction")));
    }

    [Test]
    public async Task AutoShutdown_WhenAllTorrentsComplete_TriggersAndResetsWhenActiveTorrentsFinish()
    {
        this.configService.AutoShutdownAction.Returns("Shutdown");
        this.configService.AutoShutdownCondition.Returns("WhenAllTorrentsComplete");
        this.configService.WatchFolderScanIntervalSeconds.Returns(1000);

        var activeTorrent = new Torrent
        {
            Id = 1,
            Name = "Active Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };
        this.torrentService.GetAll().Returns(new List<Torrent> { activeTorrent });

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.Status.Returns(TorrentStatus.Downloading);
        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { mockTask });

        using var lifetime = new AppLifetime(
            this.configService,
            this.eventAggregator,
            this.downloadEngine,
            this.torrentRepository,
            this.watchFolderService,
            this.networkSecurityService,
            this.rssSyncService,
            this.dynamicAuthManager,
            this.torrentService,
            powerManagementService: this.powerManagementService);

        await lifetime.StartAsync(CancellationToken.None);

        // Wait for session latch
        await Task.Delay(1500);

        // Transition torrent to stopped/finished
        mockTask.Status.Returns(TorrentStatus.Stopped);
        activeTorrent.Status = TorrentStatus.Stopped;
        activeTorrent.Progress = 1.0;

        await Task.Delay(6000);
        await lifetime.StopAsync(CancellationToken.None);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            d.ContainsKey("AutoShutdownAction") && (string)d["AutoShutdownAction"] == "None"));
        await this.powerManagementService.Received(1).ExecutePowerActionAsync(PowerAction.Shutdown);
    }

    [Test]
    public async Task StartAsync_WhenAutoStartEnabledAndAppFolderInfoProvided_RestoresTorrentsFromAppDataFolder()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leecharr_lifetime_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var torrentsDir = System.IO.Path.Combine(tempDir, "Torrents");
            System.IO.Directory.CreateDirectory(torrentsDir);
            var hash = "99887766554433221100aabbccddeeff00112233";
            var torrentFile = System.IO.Path.Combine(torrentsDir, $"{hash}.torrent");
            var expectedBytes = new byte[] { 42, 43, 44 };
            await System.IO.File.WriteAllBytesAsync(torrentFile, expectedBytes);

            var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            this.configService.AutoStart.Returns(true);
            this.configService.WatchFolderScanIntervalSeconds.Returns(1000);

            var torrent = new Torrent
            {
                Id = 5,
                Name = "Startup Restore Torrent",
                InfoHash = hash,
                Status = TorrentStatus.Downloading,
            };
            this.torrentRepository.All().Returns(new List<Torrent> { torrent });

            using var lifetime = new AppLifetime(
                this.configService,
                this.eventAggregator,
                this.downloadEngine,
                this.torrentRepository,
                this.watchFolderService,
                this.networkSecurityService,
                this.rssSyncService,
                this.dynamicAuthManager,
                this.torrentService,
                appFolderInfo: appFolderInfo);

            await lifetime.StartAsync(CancellationToken.None);
            await lifetime.StopAsync(CancellationToken.None);

            await this.downloadEngine.Received(1).AddTorrentAsync(
                Arg.Is<Torrent>(t => t.Id == 5),
                Arg.Is<byte[]>(b => b.Length == 3));
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }
    }
}
