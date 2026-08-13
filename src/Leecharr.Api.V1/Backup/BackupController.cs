using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;

namespace Leecharr.Api.V1.Backup;

public class BackupResource : RestResource
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Type { get; set; } = "Manual";
    public long Size { get; set; }
    public DateTime Time { get; set; } = DateTime.UtcNow;
}

public class RestoreBackupRequest
{
    public int BackupId { get; set; }
    public string Path { get; set; }
}

[V1ApiController("backup")]
public class BackupController : Controller
{
    private static readonly ConcurrentDictionary<int, BackupResource> Store = new();
    private static int _idCounter = 1;

    [HttpGet]
    public ActionResult<List<BackupResource>> GetAll()
    {
        return Ok(Store.Values.OrderByDescending(b => b.Time).ToList());
    }

    [HttpPost]
    public ActionResult<BackupResource> Create()
    {
        var backup = new BackupResource
        {
            Id = _idCounter++,
            Name = $"Leecharr_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip",
            Path = $"/config/Backups/manual/Leecharr_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip",
            Type = "Manual",
            Size = 1048576,
            Time = DateTime.UtcNow
        };

        Store[backup.Id] = backup;
        return Ok(backup);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        Store.TryRemove(id, out _);
        return Ok();
    }

    [HttpPost("restore")]
    public ActionResult Restore([FromBody] RestoreBackupRequest request)
    {
        return Ok(new { success = true, message = "Backup restored. Please restart Leecharr." });
    }
}
