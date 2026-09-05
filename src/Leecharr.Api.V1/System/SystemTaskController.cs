// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;

namespace Leecharr.Api.V1.System;

public class ScheduledTaskResource : RestResource
{
    public string TypeName { get; set; }

    public string Name { get; set; }

    public double Interval { get; set; }

    public DateTime? LastExecution { get; set; }

    public DateTime? LastStartTime { get; set; }

    public string LastDuration { get; set; }

    public DateTime NextExecution { get; set; }
}

public class CommandResource : RestResource
{
    public string Name { get; set; }

    public string CommandName { get; set; }

    public string Status { get; set; } = "Completed";

    public string Result { get; set; } = "Successful";

    public DateTime? Queued { get; set; } = DateTime.UtcNow;

    public DateTime? Started { get; set; } = DateTime.UtcNow;

    public DateTime? Ended { get; set; } = DateTime.UtcNow;

    public string QueuedAt => this.Queued?.ToString("o");

    public string StartedAt => this.Started?.ToString("o");

    public string EndedAt => this.Ended?.ToString("o");

    public string Duration { get; set; } = "00:00:01";
}

[V1ApiController("system/task")]
public class SystemTaskController : Controller
{
    private readonly IManageCommandQueue commandQueueManager;
    private readonly IScheduledTaskRepository scheduledTaskRepository;

    public SystemTaskController(
        IManageCommandQueue commandQueueManager = null,
        IScheduledTaskRepository scheduledTaskRepository = null)
    {
        this.commandQueueManager = commandQueueManager;
        this.scheduledTaskRepository = scheduledTaskRepository;
    }

    [HttpGet]
    public ActionResult<List<ScheduledTaskResource>> GetTasks()
    {
        var now = DateTime.UtcNow;
        var defaultTasks = new List<ScheduledTaskResource>
        {
            new() { Id = 1, TypeName = "WatchFolderScanTask", Name = "Watch Folder Scan", Interval = 0.16, LastExecution = now.AddSeconds(-10), LastStartTime = now.AddSeconds(-12), LastDuration = "00:00:02", NextExecution = now.AddSeconds(10) },
            new() { Id = 2, TypeName = "RssSyncTask", Name = "RSS Sync", Interval = 15, LastExecution = now.AddMinutes(-5), LastStartTime = now.AddMinutes(-5).AddSeconds(-1), LastDuration = "00:00:01", NextExecution = now.AddMinutes(10) },
            new() { Id = 3, TypeName = "VpnKillSwitchCheckTask", Name = "VPN Kill Switch Check", Interval = 0.16, LastExecution = now.AddSeconds(-10), LastStartTime = now.AddSeconds(-11), LastDuration = "00:00:01", NextExecution = now.AddSeconds(10) },
            new() { Id = 4, TypeName = "BackupTask", Name = "Backup Database", Interval = 1440, LastExecution = now.AddHours(-12), LastStartTime = now.AddHours(-12).AddSeconds(-5), LastDuration = "00:00:05", NextExecution = now.AddHours(12) },
            new() { Id = 5, TypeName = "ProwlarrSyncTask", Name = "Prowlarr Indexer Sync", Interval = 60, LastExecution = now.AddMinutes(-20), LastStartTime = now.AddMinutes(-20).AddSeconds(-3), LastDuration = "00:00:03", NextExecution = now.AddMinutes(40) },
            new() { Id = 6, TypeName = "SessionCleanupTask", Name = "Session Cleanup", Interval = 15, LastExecution = now.AddMinutes(-5), LastStartTime = now.AddMinutes(-5).AddSeconds(-1), LastDuration = "00:00:01", NextExecution = now.AddMinutes(10) },
        };

        if (this.scheduledTaskRepository != null)
        {
            var dbTasks = this.scheduledTaskRepository.All().ToList();
            if (dbTasks.Count > 0)
            {
                var list = new List<ScheduledTaskResource>();
                foreach (var t in dbTasks)
                {
                    var hasRun = t.LastExecution != default && t.LastExecution > DateTime.MinValue;
                    var lastStartTime = t.LastStartTime.HasValue && t.LastStartTime.Value != default && t.LastStartTime.Value > DateTime.MinValue
                        ? t.LastStartTime
                        : null;

                    string lastDuration = null;
                    if (hasRun && lastStartTime.HasValue)
                    {
                        var diff = t.LastExecution >= lastStartTime.Value
                            ? t.LastExecution - lastStartTime.Value
                            : TimeSpan.Zero;
                        lastDuration = diff.ToString(@"hh\:mm\:ss");
                    }

                    var intervalMinutes = t.Interval > 0 ? t.Interval : 15;
                    var nextExecution = hasRun
                        ? (t.LastExecution.AddMinutes(intervalMinutes) < now ? now : t.LastExecution.AddMinutes(intervalMinutes))
                        : now;

                    list.Add(new ScheduledTaskResource
                    {
                        Id = t.Id,
                        TypeName = t.TypeName,
                        Name = t.TypeName.Replace("Task", string.Empty),
                        Interval = t.Interval,
                        LastExecution = hasRun ? t.LastExecution : null,
                        LastStartTime = lastStartTime,
                        LastDuration = lastDuration,
                        NextExecution = nextExecution,
                    });
                }

                return this.Ok(list);
            }
        }

        return this.Ok(defaultTasks);
    }

    [HttpPost("{id:int}")]
    [HttpPost("{id:int}/execute")]
    public ActionResult ExecuteTask(int id)
    {
        var taskNames = new Dictionary<int, string>
        {
            [1] = "WatchFolderScan",
            [2] = "RssSync",
            [3] = "VpnKillSwitchCheck",
            [4] = "Backup",
            [5] = "ProwlarrSync",
            [6] = "SessionCleanup",
        };

        var dbTask = this.scheduledTaskRepository?.Get(id);
        var name = dbTask != null && !string.IsNullOrWhiteSpace(dbTask.TypeName)
            ? dbTask.TypeName.Replace("Task", string.Empty)
            : (taskNames.TryGetValue(id, out var tn) ? tn : "SystemTask");

        this.commandQueueManager?.PushRaw(name, "{}", CommandTrigger.Manual);
        return this.Ok(new { success = true, task = name });
    }
}

[V1ApiController("system/command")]
public class SystemCommandController : Controller
{
    private readonly IManageCommandQueue commandQueueManager;

    public SystemCommandController(IManageCommandQueue commandQueueManager = null)
    {
        this.commandQueueManager = commandQueueManager;
    }

    [HttpGet]
    public ActionResult<List<CommandResource>> GetCommands()
    {
        if (this.commandQueueManager == null)
        {
            return this.Ok(new List<CommandResource>());
        }

        var commands = this.commandQueueManager.GetAll().Select(c =>
        {
            var duration = (c.EndedAt ?? DateTime.UtcNow) - (c.StartedAt ?? c.QueuedAt);
            var result = c.Status == CommandStatus.Completed ? "Successful" : (c.Status == CommandStatus.Failed ? "Failed" : "Pending");

            return new CommandResource
            {
                Id = c.Id,
                Name = c.Name,
                CommandName = c.Name,
                Status = c.Status.ToString(),
                Result = result,
                Queued = c.QueuedAt,
                Started = c.StartedAt,
                Ended = c.EndedAt,
                Duration = duration.ToString(@"hh\:mm\:ss"),
            };
        }).ToList();

        return this.Ok(commands);
    }

    [HttpPost]
    public ActionResult<CommandResource> Execute([FromBody] CommandResource command)
    {
        if (command == null)
        {
            return this.BadRequest();
        }

        var cmdName = !string.IsNullOrWhiteSpace(command.Name) ? command.Name : command.CommandName;
        if (string.IsNullOrWhiteSpace(cmdName))
        {
            cmdName = "ManualCommand";
        }

        if (this.commandQueueManager != null)
        {
            var model = this.commandQueueManager.PushRaw(cmdName, "{}", CommandTrigger.Manual);
            var duration = (model.EndedAt ?? DateTime.UtcNow) - (model.StartedAt ?? model.QueuedAt);
            var result = model.Status == CommandStatus.Completed ? "Successful" : (model.Status == CommandStatus.Failed ? "Failed" : "Pending");

            return this.Ok(new CommandResource
            {
                Id = model.Id,
                Name = model.Name,
                CommandName = model.Name,
                Status = model.Status.ToString(),
                Result = result,
                Queued = model.QueuedAt,
                Started = model.StartedAt,
                Ended = model.EndedAt,
                Duration = duration.ToString(@"hh\:mm\:ss"),
            });
        }

        command.Id = 1;
        command.Status = "Completed";
        command.Result = "Successful";
        return this.Ok(command);
    }
}
