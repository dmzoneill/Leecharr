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
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
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
    private readonly IUdpTrackerService udpTrackerService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly ICategoryService categoryService;
    private readonly IProwlarrSyncService prowlarrSyncService;
    private readonly IManageCommandQueue commandQueueManager;
    private readonly TimeSpan backgroundLoopInterval;
    private readonly Logger logger;
    private CancellationTokenSource cts;
    private Task backgroundLoopTask;
    private Task rssLoopTask;
    private Task prowlarrLoopTask;
    private bool downloadStartedThisSession;
    private IDisposable activeSleepInhibitToken;

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
        IPowerManagementService powerManagementService = null,
        IUdpTrackerService udpTrackerService = null,
        IAppFolderInfo appFolderInfo = null,
        ICategoryService categoryService = null,
        IProwlarrSyncService prowlarrSyncService = null,
        IManageCommandQueue commandQueueManager = null,
        TimeSpan? backgroundLoopInterval = null)
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
        this.udpTrackerService = udpTrackerService;
        this.appFolderInfo = appFolderInfo;
        this.categoryService = categoryService;
        this.prowlarrSyncService = prowlarrSyncService;
        this.commandQueueManager = commandQueueManager;
        this.backgroundLoopInterval = backgroundLoopInterval ?? TimeSpan.FromSeconds(1);
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
                var pathsToTryDirs = new List<string>();
                if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
                {
                    pathsToTryDirs.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Torrents"));
                    pathsToTryDirs.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Leecharr", "Torrents"));
                }

                var legacyAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrWhiteSpace(legacyAppData))
                {
                    pathsToTryDirs.Add(Path.Combine(legacyAppData, "Torrents"));
                    pathsToTryDirs.Add(Path.Combine(legacyAppData, "Leecharr", "Torrents"));
                }

                var torrents = this.torrentRepository.All();

                foreach (var torrent in torrents)
                {
                    try
                    {
                        byte[] fileBytes = null;
                        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
                        {
                            var hash = torrent.InfoHash.ToLowerInvariant();
                            foreach (var dir in pathsToTryDirs.Distinct())
                            {
                                var path = Path.Combine(dir, $"{hash}.torrent");
                                if (File.Exists(path))
                                {
                                    fileBytes = await File.ReadAllBytesAsync(path, cancellationToken);
                                    if (fileBytes != null && fileBytes.Length > 0)
                                    {
                                        break;
                                    }
                                }
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

        try
        {
            if (this.configService.TrackerServerEnabled && this.configService.TrackerUdpEnabled && this.udpTrackerService != null)
            {
                await this.udpTrackerService.StartAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error initializing UDP tracker service on startup");
        }

        try
        {
            if (this.configService.WatchFolderEnabled)
            {
                this.watchFolderService.StartWatcher();
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error initializing watch folder service on startup");
        }

        try
        {
            if (this.prowlarrSyncService != null && this.prowlarrSyncService.IsConfigured())
            {
                this.logger.Info("Prowlarr is configured; triggering startup Prowlarr sync...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (this.commandQueueManager != null)
                        {
                            this.commandQueueManager.Push(new ProwlarrSyncCommand(), CommandTrigger.Scheduled);
                        }
                        else
                        {
                            await this.prowlarrSyncService.SyncAllAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to execute startup Prowlarr sync");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error checking Prowlarr configuration on startup");
        }

        this.cts = new CancellationTokenSource();
        this.backgroundLoopTask = Task.Run(() => this.RunBackgroundLoopAsync(this.cts.Token), this.cts.Token);
        if (this.rssSyncService != null)
        {
            this.rssLoopTask = Task.Run(() => this.RunRssSyncLoopAsync(this.cts.Token), this.cts.Token);
        }

        if (this.prowlarrSyncService != null)
        {
            this.prowlarrLoopTask = Task.Run(() => this.RunProwlarrSyncLoopAsync(this.cts.Token), this.cts.Token);
        }

        this.logger.Info("Leecharr application started");
        this.eventAggregator.PublishEvent(new ApplicationStartedEvent());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this.logger.Info("Leecharr application shutting down...");

        try
        {
            this.eventAggregator.PublishEvent(new ApplicationShutdownRequested());
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error publishing ApplicationShutdownRequested event");
        }

        if (this.udpTrackerService != null)
        {
            try
            {
                await this.udpTrackerService.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error shutting down UDP tracker service");
            }
        }

        try
        {
            this.watchFolderService.StopWatcher();
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error stopping watch folder service on shutdown");
        }

        try
        {
            if (this.cts != null)
            {
                await this.cts.CancelAsync();
            }

            var tasksToWait = new List<Task>();
            if (this.backgroundLoopTask != null)
            {
                tasksToWait.Add(this.backgroundLoopTask);
            }

            if (this.rssLoopTask != null)
            {
                tasksToWait.Add(this.rssLoopTask);
            }

            if (this.prowlarrLoopTask != null)
            {
                tasksToWait.Add(this.prowlarrLoopTask);
            }

            if (tasksToWait.Count > 0)
            {
                await Task.WhenAny(Task.WhenAll(tasksToWait), Task.Delay(5000, cancellationToken));
            }
        }
        catch
        {
        }

        try
        {
            this.activeSleepInhibitToken?.Dispose();
            this.activeSleepInhibitToken = null;
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
    }

    private async Task RunBackgroundLoopAsync(CancellationToken token)
    {
        var watchFolderTickCounter = 0;
        var maintenanceTickCounter = 0;
        var seedingTickCounter = 0;
        var lastSeedingTickUtc = DateTime.UtcNow;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(this.backgroundLoopInterval, token);

                var tasks = this.downloadEngine?.GetAllTasks()?.ToList();
                var hasDownloadingTasks = tasks != null && tasks.Any(t => t.Status == TorrentStatus.Downloading);
                if (hasDownloadingTasks)
                {
                    this.downloadStartedThisSession = true;
                    if (this.activeSleepInhibitToken == null && this.powerManagementService != null)
                    {
                        this.activeSleepInhibitToken = this.powerManagementService.InhibitSleep("Leecharr active torrent downloads in progress");
                    }
                }
                else
                {
                    if (this.activeSleepInhibitToken != null)
                    {
                        this.activeSleepInhibitToken.Dispose();
                        this.activeSleepInhibitToken = null;
                    }
                }

                // Broadcast 1-second speedPulse telemetry to SignalR clients
                if (this.signalRBroadcaster != null && this.signalRBroadcaster.IsConnected && tasks != null && tasks.Count > 0)
                {
                    try
                    {
                        var updates = new List<object>(tasks.Count);
                        foreach (var task in tasks)
                        {
                            var isInactive = task.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued;
                            var dlSpeed = isInactive ? 0 : task.DownloadSpeed;
                            var ulSpeed = isInactive ? 0 : task.UploadSpeed;
                            var dlBytes = task.DownloadedBytes;
                            var ulBytes = task.UploadedBytes;
                            var progress = task.Progress;
                            var totalBytes = task.TotalBytes > 0
                                ? task.TotalBytes
                                : (task.TotalSize > 0 ? task.TotalSize : 0);
                            var effectiveDownloaded = totalBytes > 0 && progress > 0 ? (long)(totalBytes * progress) : 0;
                            var divisor = effectiveDownloaded > 0
                                ? Math.Max(dlBytes, effectiveDownloaded)
                                : (dlBytes > 0 ? dlBytes : totalBytes);
                            var ratio = divisor > 0 ? Math.Round((double)ulBytes / divisor, 2) : 0.0;

                            long eta = 0;
                            if (!isInactive && task.Status == TorrentStatus.Downloading && dlSpeed > 0 && progress < 1.0)
                            {
                                if (totalBytes > 0)
                                {
                                    var remainingBytes = Math.Max(0, totalBytes - (long)(totalBytes * progress));
                                    eta = remainingBytes / dlSpeed;
                                }
                                else if (progress > 0.000001)
                                {
                                    var estimatedTotal = (long)Math.Min(dlBytes / progress, long.MaxValue);
                                    if (estimatedTotal > dlBytes)
                                    {
                                        eta = (estimatedTotal - dlBytes) / dlSpeed;
                                    }
                                }
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
                    var now = DateTime.UtcNow;
                    var elapsedSeconds = Math.Max(1, (long)Math.Round((now - lastSeedingTickUtc).TotalSeconds));
                    lastSeedingTickUtc = now;
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
                                    torrent.CumulativeSeedingTimeSeconds += elapsedSeconds;
                                    await this.torrentService.UpdateAsync(torrent);

                                    var category = !string.IsNullOrWhiteSpace(torrent.Category) ? this.categoryService?.GetByName(torrent.Category) : null;
                                    var effectiveRatio = torrent.TargetRatio > 0
                                        ? torrent.TargetRatio
                                        : ((category?.TargetRatio ?? 0) > 0 ? category.TargetRatio : this.configService.GlobalSeedRatioLimit);
                                    var effectiveSeedTime = torrent.TargetSeedTimeMinutes > 0 ? torrent.TargetSeedTimeMinutes : (category?.TargetSeedTimeMinutes ?? 0);

                                    var ratioReached = effectiveRatio > 0 && torrent.Ratio >= effectiveRatio;
                                    var timeReached = effectiveSeedTime > 0 && torrent.SeedTimeMinutes >= effectiveSeedTime;

                                    if (ratioReached || timeReached)
                                    {
                                        var shareAction = !string.IsNullOrWhiteSpace(torrent.ShareLimitAction) && !string.Equals(torrent.ShareLimitAction, "Default", StringComparison.OrdinalIgnoreCase)
                                            ? torrent.ShareLimitAction
                                            : this.configService.GlobalShareLimitAction;

                                        if (string.Equals(shareAction, "RemoveWithData", StringComparison.OrdinalIgnoreCase))
                                        {
                                            this.eventAggregator.PublishEvent(new TorrentSeedGoalReachedEvent(torrent));
                                            this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Removing torrent and deleting data files.", torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                            await this.torrentService.DeleteAsync(torrent.Id, deleteFiles: true);
                                        }
                                        else if (string.Equals(shareAction, "Remove", StringComparison.OrdinalIgnoreCase))
                                        {
                                            this.eventAggregator.PublishEvent(new TorrentSeedGoalReachedEvent(torrent));
                                            this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Removing torrent (preserving data).", torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                            await this.torrentService.DeleteAsync(torrent.Id, deleteFiles: false);
                                        }
                                        else if (string.Equals(shareAction, "SuperSeeding", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (torrent.InitialSeeding)
                                            {
                                                continue;
                                            }

                                            this.eventAggregator.PublishEvent(new TorrentSeedGoalReachedEvent(torrent));
                                            this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Enabling super seeding mode.", torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                            await this.torrentService.SetSuperSeedingAsync(torrent.Id, true);
                                        }
                                        else
                                        {
                                            this.eventAggregator.PublishEvent(new TorrentSeedGoalReachedEvent(torrent));
                                            this.logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Pausing seeding.", torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);

                                            await this.torrentService.PauseAsync(torrent.Id);
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
                maintenanceTickCounter++;
                if (maintenanceTickCounter >= 5)
                {
                    maintenanceTickCounter = 0;
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

                        if (hasActiveDownloads)
                        {
                            this.downloadStartedThisSession = true;
                        }

                        bool trigger = false;
                        if (string.Equals(condition, "WhenDownloadsComplete", StringComparison.OrdinalIgnoreCase))
                        {
                            trigger = this.downloadStartedThisSession && !hasActiveDownloads && allTorrents.Any(t => t.Progress >= 1.0);
                        }
                        else if (string.Equals(condition, "WhenAllTorrentsComplete", StringComparison.OrdinalIgnoreCase))
                        {
                            trigger = this.downloadStartedThisSession && !hasActiveTorrents && allTorrents.Count > 0;
                        }

                        if (trigger)
                        {
                            this.configService.SaveConfigDictionary(new Dictionary<string, object> { { "AutoShutdownAction", "None" } });
                            this.downloadStartedThisSession = false;
                            this.logger.Warn("Auto-shutdown condition met ({0}). Triggering power action: {1}", condition, powerAction);
                            try
                            {
                                await this.powerManagementService.ExecutePowerActionAsync(powerAction);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Error(ex, "Failed to execute auto-shutdown power action {0}", powerAction);
                            }
                        }
                    }
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

    private async Task RunRssSyncLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!token.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(token);

                if (this.rssSyncService != null)
                {
                    await this.rssSyncService.SyncRssFeedsAsync();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error in background RSS sync loop");
            }
        }
    }

    private async Task RunProwlarrSyncLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (!token.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(token);

                if (this.prowlarrSyncService != null && this.prowlarrSyncService.IsConfigured())
                {
                    this.logger.Info("Executing scheduled hourly Prowlarr sync...");
                    if (this.commandQueueManager != null)
                    {
                        this.commandQueueManager.Push(new ProwlarrSyncCommand(), CommandTrigger.Scheduled);
                    }
                    else
                    {
                        await this.prowlarrSyncService.SyncAllAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error in background Prowlarr sync loop");
            }
        }
    }

    public void Dispose()
    {
        this.activeSleepInhibitToken?.Dispose();
        this.activeSleepInhibitToken = null;
        this.cts?.Dispose();
    }
}
