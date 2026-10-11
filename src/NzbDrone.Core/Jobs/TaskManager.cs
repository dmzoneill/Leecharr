// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Jobs;

public class TaskManager : ITaskManager, IHandle<ApplicationStartedEvent>
{
    private static readonly (string TypeName, int Interval)[] DefaultDefinitions =
    [
        ("WatchFolderScanTask", 1),
        ("RssSyncTask", 15),
        ("VpnKillSwitchCheckTask", 1),
        ("BackupTask", 1440),
        ("ProwlarrSyncTask", 60),
        ("SessionCleanupTask", 15),
        ("BlocklistUpdateTask", 1440),
        ("GeoIpUpdateTask", 43200),
        ("DownloadHistoryCleanupTask", 1440),
        ("AutomationScheduledTask", 15),
    ];

    private readonly IScheduledTaskRepository scheduledTaskRepository;
    private readonly IConfigService configService;
    private readonly object syncRoot = new();

    public TaskManager(IScheduledTaskRepository scheduledTaskRepository, IConfigService configService)
    {
        this.scheduledTaskRepository = scheduledTaskRepository;
        this.configService = configService;
    }

    public IList<ScheduledTask> GetAll()
    {
        this.EnsureDefaultTasks();
        return this.scheduledTaskRepository.All()?.ToList() ?? new List<ScheduledTask>();
    }

    public ScheduledTask Get(int id)
    {
        return this.scheduledTaskRepository.Get(id);
    }

    public ScheduledTask GetByTypeName(string typeName)
    {
        var task = this.scheduledTaskRepository.GetByTypeName(typeName);
        if (task == null)
        {
            var def = DefaultDefinitions.FirstOrDefault(d => string.Equals(d.TypeName, typeName, StringComparison.OrdinalIgnoreCase));
            if (def.TypeName != null)
            {
                lock (this.syncRoot)
                {
                    task = this.scheduledTaskRepository.GetByTypeName(typeName)
                        ?? this.scheduledTaskRepository.GetByTypeName(def.TypeName);
                    if (task == null)
                    {
                        task = this.scheduledTaskRepository.Insert(new ScheduledTask
                        {
                            TypeName = def.TypeName,
                            Interval = this.ResolveInterval(def.TypeName, def.Interval),
                            LastExecution = DateTime.MinValue,
                            LastStartTime = null,
                        });
                    }
                }
            }
        }

        return task;
    }

    public void Update(ScheduledTask task)
    {
        this.scheduledTaskRepository.Update(task);
    }

    public void Handle(ApplicationStartedEvent message)
    {
        this.EnsureDefaultTasks();
    }

    public void EnsureDefaultTasks()
    {
        lock (this.syncRoot)
        {
            var existing = this.scheduledTaskRepository.All()?.ToList() ?? new List<ScheduledTask>();
            var existingNames = new HashSet<string>(existing.Select(t => t.TypeName), StringComparer.OrdinalIgnoreCase);

            foreach (var def in DefaultDefinitions)
            {
                if (!existingNames.Contains(def.TypeName))
                {
                    var newTask = new ScheduledTask
                    {
                        TypeName = def.TypeName,
                        Interval = this.ResolveInterval(def.TypeName, def.Interval),
                        LastExecution = DateTime.MinValue,
                        LastStartTime = null,
                    };

                    this.scheduledTaskRepository.Insert(newTask);
                    existingNames.Add(def.TypeName);
                }
            }

            this.SyncConfigurableIntervals(existing);
        }
    }

    private void SyncConfigurableIntervals(List<ScheduledTask> existing)
    {
        foreach (var task in existing)
        {
            if (!IsConfigDrivenTask(task.TypeName))
            {
                continue;
            }

            var def = DefaultDefinitions.FirstOrDefault(d => string.Equals(d.TypeName, task.TypeName, StringComparison.OrdinalIgnoreCase));
            if (def.TypeName == null)
            {
                continue;
            }

            var desiredInterval = this.ResolveInterval(task.TypeName, def.Interval);
            if (task.Interval == desiredInterval)
            {
                continue;
            }

            task.Interval = desiredInterval;
            this.scheduledTaskRepository.Update(task);
        }
    }

    private static bool IsConfigDrivenTask(string typeName)
    {
        return string.Equals(typeName, "WatchFolderScanTask", StringComparison.OrdinalIgnoreCase)
            || string.Equals(typeName, "BlocklistUpdateTask", StringComparison.OrdinalIgnoreCase);
    }

    private int ResolveInterval(string typeName, int defaultIntervalMinutes)
    {
        if (string.Equals(typeName, "WatchFolderScanTask", StringComparison.OrdinalIgnoreCase))
        {
            var seconds = this.configService.WatchFolderScanIntervalSeconds;
            if (seconds <= 0)
            {
                return defaultIntervalMinutes;
            }

            return Math.Max(1, (seconds + 59) / 60);
        }

        if (string.Equals(typeName, "BlocklistUpdateTask", StringComparison.OrdinalIgnoreCase))
        {
            var hours = this.configService.BlocklistUpdateIntervalHours;
            if (hours <= 0)
            {
                return defaultIntervalMinutes;
            }

            return Math.Max(1, hours * 60);
        }

        return defaultIntervalMinutes;
    }
}
