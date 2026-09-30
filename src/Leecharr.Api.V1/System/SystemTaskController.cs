// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

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
[Authorize(Policy = "RequireAdmin")]
public class SystemTaskController : Controller
{
    private readonly IManageCommandQueue commandQueueManager;
    private readonly IScheduledTaskRepository scheduledTaskRepository;
    private readonly ITaskManager taskManager;

    public SystemTaskController(
        IManageCommandQueue commandQueueManager = null,
        IScheduledTaskRepository scheduledTaskRepository = null,
        ITaskManager taskManager = null)
    {
        this.commandQueueManager = commandQueueManager;
        this.scheduledTaskRepository = scheduledTaskRepository;
        this.taskManager = taskManager;
    }

    [HttpGet]
    public ActionResult<List<ScheduledTaskResource>> GetTasks()
    {
        var now = DateTime.UtcNow;
        var dbTasks = this.taskManager != null
            ? this.taskManager.GetAll()
            : (this.scheduledTaskRepository?.All().ToList() ?? new List<ScheduledTask>());

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

    [HttpPost("{id:int}")]
    [HttpPost("{id:int}/execute")]
    public ActionResult ExecuteTask(int id)
    {
        var dbTask = this.taskManager?.Get(id) ?? this.scheduledTaskRepository?.Get(id);
        if (dbTask == null)
        {
            return this.NotFound("Task not found");
        }

        return this.DispatchTask(dbTask.TypeName);
    }

    [HttpPost("{typeName}")]
    [HttpPost("{typeName}/execute")]
    public ActionResult ExecuteTaskByName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return this.NotFound("Task not found");
        }

        var dbTask = this.taskManager?.GetByTypeName(typeName) ?? this.scheduledTaskRepository?.GetByTypeName(typeName);
        if (dbTask == null && !typeName.EndsWith("Task", StringComparison.OrdinalIgnoreCase))
        {
            dbTask = this.taskManager?.GetByTypeName(typeName + "Task") ?? this.scheduledTaskRepository?.GetByTypeName(typeName + "Task");
        }

        if (dbTask == null)
        {
            var all = this.taskManager?.GetAll() ?? this.scheduledTaskRepository?.All();
            dbTask = all?.FirstOrDefault(t =>
                string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.TypeName?.Replace("Task", string.Empty), typeName, StringComparison.OrdinalIgnoreCase));
        }

        var resolvedTypeName = dbTask?.TypeName;
        if (string.IsNullOrWhiteSpace(resolvedTypeName) && IsKnownTask(typeName))
        {
            resolvedTypeName = typeName;
        }

        if (string.IsNullOrWhiteSpace(resolvedTypeName))
        {
            return this.NotFound("Task not found");
        }

        return this.DispatchTask(resolvedTypeName);
    }

    private static bool IsKnownTask(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var clean = name.EndsWith("Task", StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - 4)
            : name;

        return string.Equals(clean, "WatchFolderScan", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "RssSync", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "VpnKillSwitchCheck", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "ProwlarrSync", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "Backup", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "BlocklistUpdate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "SessionCleanup", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "GeoIpUpdate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, "DownloadHistoryCleanup", StringComparison.OrdinalIgnoreCase);
    }

    private ActionResult DispatchTask(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return this.NotFound("Task not found");
        }

        var name = typeName.EndsWith("Task", StringComparison.OrdinalIgnoreCase)
            ? typeName.Substring(0, typeName.Length - 4)
            : typeName;

        if (string.Equals(name, "WatchFolderScan", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new WatchFolderScanCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "RssSync", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new RssSyncCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "VpnKillSwitchCheck", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new VpnKillSwitchCheckCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "ProwlarrSync", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new ProwlarrSyncCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "Backup", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new BackupCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "BlocklistUpdate", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new BlocklistUpdateCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "SessionCleanup", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new SessionCleanupCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "GeoIpUpdate", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new GeoIpUpdateCommand(), CommandTrigger.Manual);
        }
        else if (string.Equals(name, "DownloadHistoryCleanup", StringComparison.OrdinalIgnoreCase))
        {
            this.commandQueueManager?.Push(new DownloadHistoryCleanupCommand(), CommandTrigger.Manual);
        }
        else
        {
            this.commandQueueManager?.PushRaw(name, "{}", CommandTrigger.Manual);
        }

        return this.Ok(new { success = true, task = name });
    }
}

[V1ApiController("system/command")]
[Authorize(Policy = "RequireAdmin")]
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
            var result = c.Status == CommandStatus.Completed ? "Successful" : (c.Status == CommandStatus.Failed ? "Failed" : (c.Status == CommandStatus.Cancelled ? "Cancelled" : "Pending"));

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

    [HttpDelete("{id:int}")]
    [HttpPost("{id:int}/cancel")]
    public ActionResult Cancel(int id)
    {
        if (this.commandQueueManager == null)
        {
            return this.NotFound("Command not found");
        }

        var cancelled = this.commandQueueManager.Cancel(id);
        if (!cancelled)
        {
            return this.NotFound("Command not found");
        }

        return this.Ok(new { success = true });
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
