// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using NzbDrone.Core.SystemServices;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;
using NzbDrone.SignalR;

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
    private readonly IBroadcastSignalRMessage signalRBroadcaster;
    private readonly IQueueManagerService queueManagerService;
    private readonly IPowerManagementService powerManagementService;
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
        ITorrentService torrentService = null,
        IBroadcastSignalRMessage signalRBroadcaster = null,
        IQueueManagerService queueManagerService = null,
        IPowerManagementService powerManagementService = null)
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
        this.signalRBroadcaster = signalRBroadcaster;
        this.queueManagerService = queueManagerService;
        this.powerManagementService = powerManagementService ?? new PowerManagementService();
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
        var seedingTickCounter = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, token);

                var tasks = this.downloadEngine?.GetAllTasks()?.ToList();

                // Broadcast 1-second speedPulse telemetry to SignalR clients
                if (this.signalRBroadcaster != null && this.signalRBroadcaster.IsConnected && tasks != null && tasks.Count > 0)
                {
                    try
                    {
                        var updates = new List<object>(tasks.Count);
                        foreach (var task in tasks)
                        {
                            var dlSpeed = task.DownloadSpeed;
                            var ulSpeed = task.UploadSpeed;
                            var dlBytes = task.DownloadedBytes;
                            var ulBytes = task.UploadedBytes;
                            var progress = task.Progress;
                            var ratio = dlBytes > 0 ? Math.Round((double)ulBytes / dlBytes, 2) : 0.0;

                            long eta = 0;
                            if (task.Status == TorrentStatus.Downloading && dlSpeed > 0 && progress < 1.0)
                            {
                                var totalBytes = progress > 0 ? (long)(dlBytes / progress) : 0;
                                var remainingBytes = Math.Max(0, totalBytes - dlBytes);
                                eta = remainingBytes / dlSpeed;
                            }

                            updates.Add(new
                            {
                                id = task.TorrentId,
                                uploadSpeed = ulSpeed,
                                downloadSpeed = dlSpeed,
                                progress = progress,
                                uploaded = ulBytes,
                                downloaded = dlBytes,
                                ratio = ratio,
                                eta = eta,
                                status = task.Status.ToString(),
                                seeders = task.ConnectedSeeders,
                                leechers = task.ConnectedLeechers,
                            });
                        }

                        this.signalRBroadcaster.BroadcastMessage(new SignalRMessage
                        {
                            Name = "speedPulse",
                            Body = updates,
                        });
                    }
                    catch (Exception ex)
                    {
                        this.logger.Trace(ex, "Error broadcasting speedPulse telemetry");
                    }
                }

                // Automated seeding check (throttled to every 10s and only checks active in-memory seeding tasks)
                seedingTickCounter++;
                if (this.torrentService != null && seedingTickCounter >= 10)
                {
                    seedingTickCounter = 0;
                    try
                    {
                        var seedingTaskIds = tasks?.Where(t => t.Status == TorrentStatus.Seeding).Select(t => t.TorrentId).ToList();
                        if (seedingTaskIds != null && seedingTaskIds.Count > 0)
                        {
                            foreach (var torrentId in seedingTaskIds)
                            {
                                var torrent = this.torrentService.Get(torrentId);
                                if (torrent != null && torrent.Status == TorrentStatus.Seeding)
                                {
                                    var ratioReached = torrent.TargetRatio > 0 && torrent.Ratio >= torrent.TargetRatio;
                                    var timeReached = torrent.TargetSeedTimeMinutes > 0 && torrent.SeedTimeMinutes >= torrent.TargetSeedTimeMinutes;

                                    if (ratioReached || timeReached)
                                    {
                                        this.eventAggregator.PublishEvent(new TorrentSeedGoalReachedEvent(torrent));

                                        var shareAction = !string.IsNullOrWhiteSpace(torrent.ShareLimitAction)
                                            ? torrent.ShareLimitAction
                                            : this.configService.GlobalShareLimitAction;

                                        if (string.Equals(shareAction, "RemoveWithData", StringComparison.OrdinalIgnoreCase))
                                        {
                                            this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Removing torrent and deleting data files.", torrent.Name, torrent.Ratio, torrent.TargetRatio, torrent.SeedTimeMinutes, torrent.TargetSeedTimeMinutes);
                                            await this.torrentService.DeleteAsync(torrent.Id, deleteFiles: true);
                                        }
                                        else if (string.Equals(shareAction, "Remove", StringComparison.OrdinalIgnoreCase))
                                        {
                                            this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Removing torrent (preserving data).", torrent.Name, torrent.Ratio, torrent.TargetRatio, torrent.SeedTimeMinutes, torrent.TargetSeedTimeMinutes);
                                            await this.torrentService.DeleteAsync(torrent.Id, deleteFiles: false);
                                        }
                                        else if (string.Equals(shareAction, "SuperSeeding", StringComparison.OrdinalIgnoreCase))
                                        {
                                            this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Enabling super seeding mode.", torrent.Name, torrent.Ratio, torrent.TargetRatio, torrent.SeedTimeMinutes, torrent.TargetSeedTimeMinutes);
                                            torrent.InitialSeeding = true;
                                            await this.torrentService.UpdateAsync(torrent);
                                        }
                                        else
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

                    if (this.queueManagerService != null)
                    {
                        await this.queueManagerService.ProcessQueueAsync();
                    }

                    var autoShutdownActionStr = this.configService.AutoShutdownAction;
                    if (!string.Equals(autoShutdownActionStr, "None", StringComparison.OrdinalIgnoreCase) &&
                        Enum.TryParse<PowerAction>(autoShutdownActionStr, true, out var powerAction) &&
                        powerAction != PowerAction.None)
                    {
                        var condition = this.configService.AutoShutdownCondition;
                        var allTorrents = this.torrentService?.GetAll()?.ToList() ?? new List<Torrent>();
                        var hasActiveDownloads = allTorrents.Any(t => t.Status == TorrentStatus.Downloading);
                        var hasActiveTorrents = allTorrents.Any(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding);

                        bool trigger = false;
                        if (string.Equals(condition, "WhenDownloadsComplete", StringComparison.OrdinalIgnoreCase))
                        {
                            trigger = !hasActiveDownloads && allTorrents.Any(t => t.Progress >= 1.0);
                        }
                        else if (string.Equals(condition, "WhenAllTorrentsComplete", StringComparison.OrdinalIgnoreCase))
                        {
                            trigger = !hasActiveTorrents && allTorrents.Count > 0;
                        }

                        if (trigger)
                        {
                            this.logger.Warn("Auto-shutdown condition met ({0}). Triggering power action: {1}", condition, powerAction);
                            _ = this.powerManagementService.ExecutePowerActionAsync(powerAction);
                        }
                    }
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
