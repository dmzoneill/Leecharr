// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.SystemServices;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;

namespace NzbDrone.Host;

public class AppLifetime : IHostedService, IDisposable
{
    private readonly IAppLifetimeServices services;
    private readonly TimeSpan backgroundLoopInterval;
    private readonly Logger logger;
    private readonly ConcurrentDictionary<int, bool> superSeedingTorrents = new();
    private readonly ConcurrentDictionary<int, bool> seedGoalReachedTorrents = new();
    private readonly ConcurrentDictionary<int, bool> ratioReachedTorrents = new();
    private readonly ConcurrentDictionary<int, bool> stalledTorrents = new();
    private CancellationTokenSource cts;
    private Task backgroundLoopTask;
    private Task rssLoopTask;
    private Task prowlarrLoopTask;
    private bool downloadStartedThisSession;
    private IDisposable activeSleepInhibitToken;

    public AppLifetime(
        IAppLifetimeServices services,
        TimeSpan? backgroundLoopInterval = null)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.backgroundLoopInterval = backgroundLoopInterval ?? TimeSpan.FromSeconds(1);
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        this.logger.Info("Leecharr application starting up...");

        try
        {
            await this.services.DynamicAuthManager.InitializeConfiguredProvidersAsync();
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error initializing dynamic authentication providers on startup");
        }

        try
        {
            await this.services.DownloadEngine.StartAsync();

            var pathsToTryDirs = new List<string>();
            if (this.services.AppFolderInfo != null && !string.IsNullOrWhiteSpace(this.services.AppFolderInfo.AppDataFolder))
            {
                pathsToTryDirs.Add(Path.Combine(this.services.AppFolderInfo.AppDataFolder, "Torrents"));
                pathsToTryDirs.Add(Path.Combine(this.services.AppFolderInfo.AppDataFolder, "Leecharr", "Torrents"));
            }

            var legacyAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(legacyAppData))
            {
                pathsToTryDirs.Add(Path.Combine(legacyAppData, "Torrents"));
                pathsToTryDirs.Add(Path.Combine(legacyAppData, "Leecharr", "Torrents"));
            }

            var torrents = this.services.TorrentRepository.All();

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

                    await this.services.DownloadEngine.AddTorrentAsync(torrent, fileBytes);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to restore torrent {0} into engine on startup", torrent.Name);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error initializing download engine on startup");
        }

        try
        {
            if (this.services.ConfigService.TrackerServerEnabled && this.services.ConfigService.TrackerUdpEnabled && this.services.UdpTrackerService != null)
            {
                await this.services.UdpTrackerService.StartAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error initializing UDP tracker service on startup");
        }

        try
        {
            if (this.services.ConfigService.WatchFolderEnabled)
            {
                this.services.WatchFolderService.StartWatcher();
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error initializing watch folder service on startup");
        }

        try
        {
            if (this.services.ProwlarrSyncService != null && this.services.ProwlarrSyncService.IsConfigured())
            {
                this.logger.Info("Prowlarr is configured; triggering startup Prowlarr sync...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (this.services.CommandQueueManager != null)
                        {
                            this.services.CommandQueueManager.Push(new ProwlarrSyncCommand(), CommandTrigger.Scheduled);
                        }
                        else
                        {
                            await this.services.ProwlarrSyncService.SyncAllAsync();
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
        if (this.services.RssSyncService != null)
        {
            this.rssLoopTask = Task.Run(() => this.RunRssSyncLoopAsync(this.cts.Token), this.cts.Token);
        }

        if (this.services.ProwlarrSyncService != null)
        {
            this.prowlarrLoopTask = Task.Run(() => this.RunProwlarrSyncLoopAsync(this.cts.Token), this.cts.Token);
        }

        this.logger.Info("Leecharr application started");
        this.services.EventAggregator.PublishEvent(new ApplicationStartedEvent());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this.logger.Info("Leecharr application shutting down...");

        try
        {
            this.services.EventAggregator.PublishEvent(new ApplicationShutdownRequested());
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error publishing ApplicationShutdownRequested event");
        }

        if (this.services.UdpTrackerService != null)
        {
            try
            {
                await this.services.UdpTrackerService.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error shutting down UDP tracker service");
            }
        }

        try
        {
            this.services.WatchFolderService.StopWatcher();
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
            await this.services.DownloadEngine.StopAsync();
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error shutting down download engine");
        }

        try
        {
            if (this.services.Database?.DatabaseType == DatabaseType.SQLite)
            {
                using var conn = this.services.Database.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }
            else if (this.services.Database?.DatabaseType == DatabaseType.PostgreSQL)
            {
                Npgsql.NpgsqlConnection.ClearAllPools();
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error performing database cleanup and pool clear on shutdown");
        }
    }

    public void Dispose()
    {
        this.activeSleepInhibitToken?.Dispose();
        this.activeSleepInhibitToken = null;
        this.cts?.Dispose();
    }

    private async Task RunBackgroundLoopAsync(CancellationToken token)
    {
        var watchFolderTickCounter = 0;
        var maintenanceTickCounter = 0;
        var walCheckpointTickCounter = 0;
        var seedingTickCounter = 0;
        var lastSeedingTickUtc = DateTime.UtcNow;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(this.backgroundLoopInterval, token);

                var tasks = this.services.DownloadEngine?.GetAllTasks()?.ToList();
                var hasDownloadingTasks = tasks != null && tasks.Any(t => t.Status == TorrentStatus.Downloading);
                if (hasDownloadingTasks)
                {
                    this.downloadStartedThisSession = true;
                    if (this.activeSleepInhibitToken == null && this.services.PowerManagementService != null)
                    {
                        this.activeSleepInhibitToken = this.services.PowerManagementService.InhibitSleep("Leecharr active torrent downloads in progress");
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
                if (this.services.SignalRBroadcaster != null && this.services.SignalRBroadcaster.IsConnected && tasks != null && tasks.Count > 0)
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

                        this.services.SignalRBroadcaster.BroadcastMessage(new SignalRMessage
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
                if (this.services.TorrentService != null && seedingTickCounter >= 10)
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
                                var torrent = this.services.TorrentService.Get(torrentId);
                                if (torrent != null && torrent.Status == TorrentStatus.Seeding)
                                {
                                    torrent.CumulativeSeedingTimeSeconds += elapsedSeconds;
                                    await this.services.TorrentService.UpdateAsync(torrent);

                                    var category = !string.IsNullOrWhiteSpace(torrent.Category) ? this.services.CategoryService?.GetByName(torrent.Category) : null;
                                    var serviceRatio = this.services.TorrentService?.GetEffectiveTargetRatio(torrent) ?? 0;
                                    var effectiveRatio = serviceRatio > 0
                                        ? serviceRatio
                                        : (torrent.TargetRatio > 0 ? torrent.TargetRatio : ((category?.TargetRatio ?? 0) > 0 ? category.TargetRatio : (this.services.ConfigService?.GlobalSeedRatioLimit ?? 0)));
                                    var serviceSeedTime = this.services.TorrentService?.GetEffectiveTargetSeedTimeMinutes(torrent) ?? 0;
                                    var effectiveSeedTime = serviceSeedTime > 0
                                        ? serviceSeedTime
                                        : (torrent.TargetSeedTimeMinutes > 0 ? torrent.TargetSeedTimeMinutes : (category?.TargetSeedTimeMinutes ?? 0));

                                    var ratioReached = effectiveRatio > 0 && torrent.Ratio >= effectiveRatio;
                                    var timeReached = effectiveSeedTime > 0 && torrent.SeedTimeMinutes >= effectiveSeedTime;

                                    if (ratioReached || timeReached)
                                    {
                                        if (ratioReached && this.ratioReachedTorrents.TryAdd(torrent.Id, true))
                                        {
                                            this.services.EventAggregator?.PublishEvent(new TorrentRatioReachedEvent(torrent, torrent.Ratio));
                                        }

                                        if (this.seedGoalReachedTorrents.TryAdd(torrent.Id, true))
                                        {
                                            this.services.EventAggregator?.PublishEvent(new TorrentSeedGoalReachedEvent(torrent));
                                        }

                                        var shareAction = !string.IsNullOrWhiteSpace(torrent.ShareLimitAction) && !string.Equals(torrent.ShareLimitAction, "Default", StringComparison.OrdinalIgnoreCase)
                                            ? torrent.ShareLimitAction
                                            : this.services.ConfigService.GlobalShareLimitAction;

                                        if (string.Equals(shareAction, "RemoveWithData", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!torrent.IsImported)
                                            {
                                                this.logger.Info("[State Machine] Torrent #{0} ('{1}') reached seed goal (Ratio: {2:F2}/{3:F2}, SeedTime: {4}/{5}m), but Servarr import is pending. Pausing seeding and deferring removal until Servarr import completes.", torrent.Id, torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                                await this.services.TorrentService.PauseAsync(torrent.Id, $"Seed goal reached (Ratio: {torrent.Ratio:F2}/{effectiveRatio:F2}, SeedTime: {torrent.SeedTimeMinutes}/{effectiveSeedTime}m); Servarr import pending");
                                            }
                                            else
                                            {
                                                this.logger.Info("[State Machine] Torrent #{0} ('{1}') reached seed goal (Ratio: {2:F2}/{3:F2}, SeedTime: {4}/{5}m). Removing torrent and deleting data files.", torrent.Id, torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                                this.ratioReachedTorrents.TryRemove(torrent.Id, out _);
                                                this.seedGoalReachedTorrents.TryRemove(torrent.Id, out _);
                                                this.superSeedingTorrents.TryRemove(torrent.Id, out _);
                                                await this.services.TorrentService.DeleteAsync(torrent.Id, deleteFiles: true);
                                            }
                                        }
                                        else if (string.Equals(shareAction, "Remove", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!torrent.IsImported)
                                            {
                                                this.logger.Info("[State Machine] Torrent #{0} ('{1}') reached seed goal (Ratio: {2:F2}/{3:F2}, SeedTime: {4}/{5}m), but Servarr import is pending. Pausing seeding and deferring removal until Servarr import completes.", torrent.Id, torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                                await this.services.TorrentService.PauseAsync(torrent.Id, $"Seed goal reached (Ratio: {torrent.Ratio:F2}/{effectiveRatio:F2}, SeedTime: {torrent.SeedTimeMinutes}/{effectiveSeedTime}m); Servarr import pending");
                                            }
                                            else
                                            {
                                                this.logger.Info("[State Machine] Torrent #{0} ('{1}') reached seed goal (Ratio: {2:F2}/{3:F2}, SeedTime: {4}/{5}m). Removing torrent (preserving data).", torrent.Id, torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                                this.ratioReachedTorrents.TryRemove(torrent.Id, out _);
                                                this.seedGoalReachedTorrents.TryRemove(torrent.Id, out _);
                                                this.superSeedingTorrents.TryRemove(torrent.Id, out _);
                                                await this.services.TorrentService.DeleteAsync(torrent.Id, deleteFiles: false);
                                            }
                                        }
                                        else if (string.Equals(shareAction, "SuperSeeding", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var matchingTask = tasks?.FirstOrDefault(t => t.TorrentId == torrent.Id);
                                            var isSuperSeeding = matchingTask?.IsSuperSeeding ?? torrent.InitialSeeding;

                                            if (isSuperSeeding)
                                            {
                                                this.superSeedingTorrents.TryAdd(torrent.Id, true);
                                                continue;
                                            }

                                            if (this.superSeedingTorrents.TryRemove(torrent.Id, out _))
                                            {
                                                this.logger.Info("[State Machine] Torrent #{0} ('{1}') reached seed goal and completed super seeding mode. Pausing seeding.", torrent.Id, torrent.Name);
                                                await this.services.TorrentService.PauseAsync(torrent.Id, "Seed goal reached and completed super seeding mode");
                                            }
                                            else
                                            {
                                                this.logger.Info("[State Machine] Torrent #{0} ('{1}') reached seed goal (Ratio: {2:F2}/{3:F2}, SeedTime: {4}/{5}m). Enabling super seeding mode.", torrent.Id, torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                                this.superSeedingTorrents.TryAdd(torrent.Id, true);
                                                await this.services.TorrentService.SetSuperSeedingAsync(torrent.Id, true);
                                            }
                                        }
                                        else
                                        {
                                            this.logger.Info("[State Machine] Torrent #{0} ('{1}') reached seed goal (Ratio: {2:F2}/{3:F2}, SeedTime: {4}/{5}m). Pausing seeding.", torrent.Id, torrent.Name, torrent.Ratio, effectiveRatio, torrent.SeedTimeMinutes, effectiveSeedTime);
                                            await this.services.TorrentService.PauseAsync(torrent.Id, $"Seed goal reached (Ratio: {torrent.Ratio:F2}/{effectiveRatio:F2}, SeedTime: {torrent.SeedTimeMinutes}/{effectiveSeedTime}m)");
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
                var watchInterval = this.services.ConfigService.WatchFolderScanIntervalSeconds > 0
                    ? this.services.ConfigService.WatchFolderScanIntervalSeconds
                    : 10;

                if (watchFolderTickCounter >= watchInterval)
                {
                    watchFolderTickCounter = 0;
                    await this.services.WatchFolderService.ScanWatchFolderAsync();
                }

                // 2. Check VPN Kill Switch every 5 seconds
                maintenanceTickCounter++;
                if (maintenanceTickCounter >= 5)
                {
                    maintenanceTickCounter = 0;
                    this.services.NetworkSecurityService.CheckVpnKillSwitch();

                    walCheckpointTickCounter++;
                    if (walCheckpointTickCounter >= 12)
                    {
                        walCheckpointTickCounter = 0;
                        try
                        {
                            if (this.services.Database?.DatabaseType == DatabaseType.SQLite)
                            {
                                using var conn = this.services.Database.OpenConnection();
                                using var cmd = conn.CreateCommand();
                                cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch (Exception ex)
                        {
                            this.logger.Debug(ex, "Error performing periodic SQLite WAL checkpoint");
                        }
                    }

                    if (this.services.QueueManagerService != null)
                    {
                        await this.services.QueueManagerService.ProcessQueueAsync();
                    }

                    var autoShutdownActionStr = this.services.ConfigService.AutoShutdownAction;
                    if (!string.Equals(autoShutdownActionStr, "None", StringComparison.OrdinalIgnoreCase) &&
                        Enum.TryParse<PowerAction>(autoShutdownActionStr, true, out var powerAction) &&
                        powerAction != PowerAction.None)
                    {
                        var condition = this.services.ConfigService.AutoShutdownCondition;
                        var allTorrents = this.services.TorrentService?.GetAll()?.ToList() ?? new List<Torrent>();
                        var hasActiveDownloads = allTorrents.Any(t => t.Status == TorrentStatus.Downloading);
                        var hasActiveTorrents = allTorrents.Any(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding);

                        if (hasActiveDownloads)
                        {
                            this.downloadStartedThisSession = true;
                        }

                        var trigger = false;
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
                            this.services.ConfigService.SaveConfigDictionary(new Dictionary<string, object> { { "AutoShutdownAction", "None" } });
                            this.downloadStartedThisSession = false;
                            this.logger.Warn("Auto-shutdown condition met ({0}). Triggering power action: {1}", condition, powerAction);
                            try
                            {
                                await this.services.PowerManagementService.ExecutePowerActionAsync(powerAction);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Error(ex, "Failed to execute auto-shutdown power action {0}", powerAction);
                            }
                        }
                    }

                    // 3. Automation Watchdog: Disk space, stalled states, and speed limits
                    try
                    {
                        if (this.services.DiskSpaceService != null)
                        {
                            this.services.DiskSpaceService.CheckDiskSpaceThresholds();
                        }

                        if (tasks != null && tasks.Count > 0)
                        {
                            long totalDlSpeed = 0;
                            long totalUlSpeed = 0;
                            var activeTasksCount = 0;

                            foreach (var task in tasks)
                            {
                                totalDlSpeed += task.DownloadSpeed;
                                totalUlSpeed += task.UploadSpeed;

                                if (task.Status == TorrentStatus.Downloading || task.Status == TorrentStatus.Seeding)
                                {
                                    activeTasksCount++;
                                }

                                if (task.Status == TorrentStatus.Downloading && task.DownloadSpeed == 0 && task.Progress < 1.0)
                                {
                                    var torrent = this.services.TorrentService?.Get(task.TorrentId);
                                    if (torrent != null)
                                    {
                                        var minutes = (DateTime.UtcNow - torrent.DateAdded).TotalMinutes;
                                        if (minutes >= 5)
                                        {
                                            if (this.stalledTorrents.TryAdd(torrent.Id, true))
                                            {
                                                this.services.EventAggregator?.PublishEvent(new TorrentStalledEvent(torrent, (int)minutes));
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    this.stalledTorrents.TryRemove(task.TorrentId, out _);
                                }
                            }

                            var maxDl = this.services.ConfigService.MaxDownloadSpeedKbps > 0 ? (long)this.services.ConfigService.MaxDownloadSpeedKbps * 1024L : 0;
                            var maxUl = this.services.ConfigService.MaxUploadSpeedKbps > 0 ? (long)this.services.ConfigService.MaxUploadSpeedKbps * 1024L : 0;

                            if ((maxDl > 0 && totalDlSpeed >= maxDl) || (maxUl > 0 && totalUlSpeed >= maxUl))
                            {
                                this.services.EventAggregator?.PublishEvent(new SpeedThresholdExceededEvent(totalDlSpeed, totalUlSpeed, activeTasksCount));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Trace(ex, "Error executing automation watchdog checks");
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

                if (this.services.RssSyncService != null)
                {
                    await this.services.RssSyncService.SyncRssFeedsAsync();
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

                if (this.services.ProwlarrSyncService != null && this.services.ProwlarrSyncService.IsConfigured())
                {
                    this.logger.Info("Executing scheduled hourly Prowlarr sync...");
                    if (this.services.CommandQueueManager != null)
                    {
                        this.services.CommandQueueManager.Push(new ProwlarrSyncCommand(), CommandTrigger.Scheduled);
                    }
                    else
                    {
                        await this.services.ProwlarrSyncService.SyncAllAsync();
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
}
