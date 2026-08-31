// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http.Authentication;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace NzbDrone.Host;

public class AppLifetime : IHostedService, IDisposable
{
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly IDownloadEngine downloadEngine;
    private readonly ITorrentRepository torrentRepository;
    private readonly IWatchFolderService watchFolderService;
    private readonly INetworkSecurityService networkSecurityService;
    private readonly IRssSyncService rssSyncService;
    private readonly IDynamicAuthSchemeManager dynamicAuthManager;
    private readonly ITorrentService torrentService;
    private readonly Logger logger;
    private CancellationTokenSource cts;
    private Task backgroundLoopTask;

    public AppLifetime(
        IConfigService configService,
        IEventAggregator eventAggregator,
        IDownloadEngine downloadEngine,
        ITorrentRepository torrentRepository,
        IWatchFolderService watchFolderService,
        INetworkSecurityService networkSecurityService,
        IRssSyncService rssSyncService,
        IDynamicAuthSchemeManager dynamicAuthManager,
        ITorrentService torrentService = null)
    {
        this.configService = configService;
        this.eventAggregator = eventAggregator;
        this.downloadEngine = downloadEngine;
        this.torrentRepository = torrentRepository;
        this.watchFolderService = watchFolderService;
        this.networkSecurityService = networkSecurityService;
        this.rssSyncService = rssSyncService;
        this.dynamicAuthManager = dynamicAuthManager;
        this.torrentService = torrentService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        this.logger.Info("Leecharr application starting up...");

        try
        {
            await this.dynamicAuthManager.InitializeConfiguredProvidersAsync();
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error initializing dynamic authentication providers on startup");
        }

        try
        {
            await this.downloadEngine.StartAsync();

            if (this.configService.AutoStart)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var torrentsDir1 = Path.Combine(appData, "Torrents");
                var torrentsDir2 = Path.Combine(appData, "Leecharr", "Torrents");
                var torrents = this.torrentRepository.All();

                foreach (var torrent in torrents)
                {
                    try
                    {
                        byte[] fileBytes = null;
                        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
                        {
                            var hash = torrent.InfoHash.ToLowerInvariant();
                            var path1 = Path.Combine(torrentsDir1, $"{hash}.torrent");
                            var path2 = Path.Combine(torrentsDir2, $"{hash}.torrent");
                            if (File.Exists(path1))
                            {
                                fileBytes = await File.ReadAllBytesAsync(path1, cancellationToken);
                            }
                            else if (File.Exists(path2))
                            {
                                fileBytes = await File.ReadAllBytesAsync(path2, cancellationToken);
                            }
                        }

                        await this.downloadEngine.AddTorrentAsync(torrent, fileBytes);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to restore torrent {0} into engine on startup", torrent.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error initializing download engine on startup");
        }

        this.cts = new CancellationTokenSource();
        this.backgroundLoopTask = Task.Run(() => this.RunBackgroundLoopAsync(this.cts.Token), this.cts.Token);

        this.logger.Info("Leecharr application started");
        this.eventAggregator.PublishEvent(new ApplicationStartedEvent());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this.logger.Info("Leecharr application shutting down...");

        try
        {
            if (this.cts != null)
            {
                await this.cts.CancelAsync();
            }

            if (this.backgroundLoopTask != null)
            {
                await Task.WhenAny(this.backgroundLoopTask, Task.Delay(5000, cancellationToken));
            }
        }
        catch
        {
        }

        try
        {
            await this.downloadEngine.StopAsync();
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error shutting down download engine");
        }

        this.eventAggregator.PublishEvent(new ApplicationShutdownRequested());
    }

    private async Task RunBackgroundLoopAsync(CancellationToken token)
    {
        var watchFolderTickCounter = 0;
        var rssTickCounter = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, token);

                // Automated seeding check
                if (this.torrentService != null)
                {
                    try
                    {
                        var torrents = this.torrentService.GetAll();
                        foreach (var torrent in torrents)
                        {
                            if (torrent.Status == TorrentStatus.Seeding)
                            {
                                var ratioReached = torrent.TargetRatio > 0 && torrent.Ratio >= torrent.TargetRatio;
                                var timeReached = torrent.TargetSeedTimeMinutes > 0 && torrent.SeedTimeMinutes >= torrent.TargetSeedTimeMinutes;

                                if (ratioReached || timeReached)
                                {
                                    this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Pausing seeding.", torrent.Name, torrent.Ratio, torrent.TargetRatio, torrent.SeedTimeMinutes, torrent.TargetSeedTimeMinutes);

                                    var oldStatus = torrent.Status;
                                    await this.torrentService.PauseAsync(torrent.Id);
                                    this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                                    {
                                        Torrent = torrent,
                                        OldStatus = oldStatus,
                                        NewStatus = TorrentStatus.Stopped,
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Error checking seed goals in background loop");
                    }
                }

                // 1. Scan watch folder according to configured interval
                watchFolderTickCounter++;
                var watchInterval = this.configService.WatchFolderScanIntervalSeconds > 0
                    ? this.configService.WatchFolderScanIntervalSeconds
                    : 10;

                if (watchFolderTickCounter >= watchInterval)
                {
                    watchFolderTickCounter = 0;
                    await this.watchFolderService.ScanWatchFolderAsync();
                }

                // 2. Check VPN Kill Switch every 5 seconds
                if (watchFolderTickCounter % 5 == 0)
                {
                    this.networkSecurityService.CheckVpnKillSwitch();
                }

                // 3. RSS Sync every ~15 minutes (900 seconds)
                rssTickCounter++;
                if (rssTickCounter >= 900)
                {
                    rssTickCounter = 0;
                    await this.rssSyncService.SyncRssFeedsAsync();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error in background maintenance loop");
            }
        }
    }

    public void Dispose()
    {
        this.cts?.Dispose();
    }
}
