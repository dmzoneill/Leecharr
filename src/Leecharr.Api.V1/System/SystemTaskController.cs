using System;
using System.Collections.Generic;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;

namespace Leecharr.Api.V1.System;

public class ScheduledTaskResource : RestResource
{
    public string TypeName { get; set; }
    public string Name { get; set; }
    public double Interval { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastStartTime { get; set; }
    public DateTime NextExecution { get; set; }
}

public class CommandResource : RestResource
{
    public string Name { get; set; }
    public string CommandName { get; set; }
    public string Status { get; set; } = "Completed";
    public string Result { get; set; } = "Successful";
    public DateTime Queued { get; set; } = DateTime.UtcNow;
    public DateTime Started { get; set; } = DateTime.UtcNow;
    public DateTime Ended { get; set; } = DateTime.UtcNow;
    public string Duration { get; set; } = "00:00:01";
}

[V1ApiController("system/task")]
public class SystemTaskController : Controller
{
    [HttpGet]
    public ActionResult<List<ScheduledTaskResource>> GetTasks()
    {
        var now = DateTime.UtcNow;
        var list = new List<ScheduledTaskResource>
        {
            new() { Id = 1, TypeName = "WatchFolderScanTask", Name = "Watch Folder Scan", Interval = 0.16, LastExecution = now.AddSeconds(-10), LastStartTime = now.AddSeconds(-10), NextExecution = now.AddSeconds(10) },
            new() { Id = 2, TypeName = "RssSyncTask", Name = "RSS Sync", Interval = 15, LastExecution = now.AddMinutes(-5), LastStartTime = now.AddMinutes(-5), NextExecution = now.AddMinutes(10) },
            new() { Id = 3, TypeName = "VpnKillSwitchCheckTask", Name = "VPN Kill Switch Check", Interval = 0.16, LastExecution = now.AddSeconds(-10), LastStartTime = now.AddSeconds(-10), NextExecution = now.AddSeconds(10) },
            new() { Id = 4, TypeName = "BackupTask", Name = "Backup Database", Interval = 1440, LastExecution = now.AddHours(-12), LastStartTime = now.AddHours(-12), NextExecution = now.AddHours(12) },
            new() { Id = 5, TypeName = "ProwlarrSyncTask", Name = "Prowlarr Indexer Sync", Interval = 60, LastExecution = now.AddMinutes(-20), LastStartTime = now.AddMinutes(-20), NextExecution = now.AddMinutes(40) }
        };

        return Ok(list);
    }
}

[V1ApiController("system/command")]
public class SystemCommandController : Controller
{
    [HttpGet]
    public ActionResult<List<CommandResource>> GetCommands()
    {
        return Ok(new List<CommandResource>());
    }

    [HttpPost]
    public ActionResult<CommandResource> Execute([FromBody] CommandResource command)
    {
        if (command == null)
        {
            return BadRequest();
        }

        command.Id = 1;
        command.Status = "Completed";
        command.Result = "Successful";
        return Ok(command);
    }
}
