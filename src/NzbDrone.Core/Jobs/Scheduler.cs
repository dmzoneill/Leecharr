// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace NzbDrone.Core.Jobs;

public class Scheduler : BackgroundService
{
    private readonly ITaskManager taskManager;
    private readonly IManageCommandQueue commandQueueManager;
    private readonly Logger logger;

    public Scheduler(ITaskManager taskManager, IManageCommandQueue commandQueueManager = null)
    {
        this.taskManager = taskManager;
        this.commandQueueManager = commandQueueManager;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.Info("Scheduler started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tasks = this.taskManager?.GetAll();
                if (tasks != null)
                {
                    var now = DateTime.UtcNow;
                    foreach (var task in tasks)
                    {
                        var interval = task.Interval > 0 ? task.Interval : 15;
                        var dueAt = task.LastExecution.AddMinutes(interval);

                        if (task.LastExecution == default || task.LastExecution == DateTime.MinValue || dueAt <= now)
                        {
                            this.logger.Debug("Executing scheduled task: {0}", task.TypeName);
                            var startTime = DateTime.UtcNow;
                            task.LastStartTime = startTime;
                            this.taskManager.Update(task);

                            try
                            {
                                this.DispatchTask(task);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Error(ex, "Scheduled task dispatch failed: {0}", task.TypeName);
                            }
                            finally
                            {
                                task.LastStartTime = startTime;
                                task.LastExecution = DateTime.UtcNow;
                                this.taskManager.Update(task);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Scheduler tick error");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void DispatchTask(ScheduledTask task)
    {
        var name = !string.IsNullOrWhiteSpace(task.TypeName)
            ? task.TypeName.Replace("Task", string.Empty)
            : "SystemTask";

        if (this.commandQueueManager == null)
        {
            return;
        }

        if (string.Equals(name, "WatchFolderScan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "WatchFolderScanTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new WatchFolderScanCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "RssSync", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "RssSyncTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new RssSyncCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "VpnKillSwitchCheck", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "VpnKillSwitchCheckTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new VpnKillSwitchCheckCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "ProwlarrSync", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "ProwlarrSyncTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new ProwlarrSyncCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "Backup", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "BackupTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new BackupCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "BlocklistUpdate", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "BlocklistUpdateTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new BlocklistUpdateCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "SessionCleanup", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "SessionCleanupTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new SessionCleanupCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "GeoIpUpdate", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "GeoIpUpdateTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new GeoIpUpdateCommand(), CommandTrigger.Scheduled);
        }
        else if (string.Equals(name, "DownloadHistoryCleanup", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "DownloadHistoryCleanupTask", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager.Push(new DownloadHistoryCleanupCommand(), CommandTrigger.Scheduled);
        }
        else
        {
            this.commandQueueManager.PushRaw(name, "{}", CommandTrigger.Scheduled);
        }
    }
}
