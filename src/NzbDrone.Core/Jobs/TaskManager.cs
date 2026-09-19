// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
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
    ];

    private readonly IScheduledTaskRepository scheduledTaskRepository;
    private readonly object syncRoot = new();

    public TaskManager(IScheduledTaskRepository scheduledTaskRepository)
    {
        this.scheduledTaskRepository = scheduledTaskRepository;
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
                    task = this.scheduledTaskRepository.GetByTypeName(typeName);
                    if (task == null)
                    {
                        task = this.scheduledTaskRepository.Insert(new ScheduledTask
                        {
                            TypeName = def.TypeName,
                            Interval = def.Interval,
                            LastExecution = DateTime.UtcNow,
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
                        Interval = def.Interval,
                        LastExecution = DateTime.UtcNow,
                        LastStartTime = null,
                    };

                    this.scheduledTaskRepository.Insert(newTask);
                    existingNames.Add(def.TypeName);
                }
            }
        }
    }
}
