// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

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
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public BackupController(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.diskProvider = diskProvider;
    }

    private List<BackupResource> GetBackupsInternal()
    {
        var backupDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups");
        var list = new List<BackupResource>();

        if (!Directory.Exists(backupDir))
        {
            return list;
        }

        var files = Directory.GetFiles(backupDir, "*.zip", SearchOption.AllDirectories);
        var id = 1;
        foreach (var file in files.OrderByDescending(global::System.IO.File.GetLastWriteTimeUtc))
        {
            var fi = new FileInfo(file);
            var isManual = file.Contains("manual", StringComparison.OrdinalIgnoreCase);
            list.Add(new BackupResource
            {
                Id = id++,
                Name = fi.Name,
                Path = fi.FullName,
                Size = fi.Length,
                Time = fi.LastWriteTimeUtc,
                Type = isManual ? "Manual" : "Scheduled",
            });
        }

        return list;
    }

    [HttpGet]
    public ActionResult<List<BackupResource>> GetAll()
    {
        return this.Ok(this.GetBackupsInternal());
    }

    [HttpPost]
    public ActionResult<BackupResource> Create()
    {
        try
        {
            var targetDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups", "manual");
            Directory.CreateDirectory(targetDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var zipName = $"Leecharr_backup_{timestamp}.zip";
            var zipPath = Path.Combine(targetDir, zipName);

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var dbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
                if (global::System.IO.File.Exists(dbPath))
                {
                    zip.CreateEntryFromFile(dbPath, "leecharr.db");
                }

                var configPath = Path.Combine(this.appFolderInfo.AppDataFolder, "config.xml");
                if (global::System.IO.File.Exists(configPath))
                {
                    zip.CreateEntryFromFile(configPath, "config.xml");
                }
            }

            var fi = new FileInfo(zipPath);
            var backup = new BackupResource
            {
                Id = 1,
                Name = zipName,
                Path = zipPath,
                Type = "Manual",
                Size = fi.Length,
                Time = fi.LastWriteTimeUtc,
            };

            this.logger.Info("Created manual backup archive at {0} ({1} bytes)", zipPath, fi.Length);
            return this.Ok(backup);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to create backup archive");
            return this.StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        var backups = this.GetBackupsInternal();
        var match = backups.FirstOrDefault(b => b.Id == id);
        if (match != null && global::System.IO.File.Exists(match.Path))
        {
            try
            {
                global::System.IO.File.Delete(match.Path);
                this.logger.Info("Deleted backup archive: {0}", match.Path);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to delete backup archive: {0}", match.Path);
            }
        }

        return this.Ok();
    }

    [HttpPost("restore")]
    public ActionResult Restore([FromBody] RestoreBackupRequest request)
    {
        if (request == null)
        {
            return this.BadRequest(new { success = false, message = "Invalid request." });
        }

        var backups = this.GetBackupsInternal();
        var backup = backups.FirstOrDefault(b => b.Id == request.BackupId || (!string.IsNullOrWhiteSpace(request.Path) && string.Equals(b.Path, request.Path, StringComparison.OrdinalIgnoreCase)));
        if (backup == null || !global::System.IO.File.Exists(backup.Path))
        {
            return this.BadRequest(new { success = false, message = "Backup not found." });
        }

        try
        {
            using (var zip = ZipFile.OpenRead(backup.Path))
            {
                foreach (var entry in zip.Entries)
                {
                    var fileName = Path.GetFileName(entry.FullName);
                    if (string.Equals(fileName, "leecharr.db", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, "config.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(this.appFolderInfo.AppDataFolder, fileName);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }
            }

            this.logger.Info("Restored backup archive from {0}", backup.Path);
            return this.Ok(new { success = true, message = "Backup restored successfully. Please restart Leecharr." });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to restore backup archive from {0}", backup.Path);
            return this.StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
