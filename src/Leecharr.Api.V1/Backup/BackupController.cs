// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
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
    public int? BackupId { get; set; }

    public string FileName { get; set; }

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

    [HttpGet("{id:int}/download")]
    public ActionResult Download(int id)
    {
        var backups = this.GetBackupsInternal();
        var backup = backups.FirstOrDefault(b => b.Id == id);
        if (backup == null || !global::System.IO.File.Exists(backup.Path))
        {
            return this.NotFound();
        }

        var stream = global::System.IO.File.OpenRead(backup.Path);
        return this.File(stream, "application/zip", backup.Name);
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

            var dbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
            var walPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db-wal");

            if (global::System.IO.File.Exists(dbPath))
            {
                try
                {
                    using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                    {
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        cmd.ExecuteNonQuery();
                    }

                    SqliteConnection.ClearAllPools();
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to execute SQLite WAL checkpoint on {0}", dbPath);
                }
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                if (global::System.IO.File.Exists(dbPath))
                {
                    zip.CreateEntryFromFile(dbPath, "leecharr.db");
                }

                if (global::System.IO.File.Exists(walPath))
                {
                    zip.CreateEntryFromFile(walPath, "leecharr.db-wal");
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
        var backup = backups.FirstOrDefault(b =>
            (request.BackupId.HasValue && request.BackupId.Value > 0 && b.Id == request.BackupId.Value) ||
            (!string.IsNullOrWhiteSpace(request.Path) && string.Equals(b.Path, request.Path, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(request.FileName) && (string.Equals(b.Name, request.FileName, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(b.Path), request.FileName, StringComparison.OrdinalIgnoreCase))));
        if (backup == null || !global::System.IO.File.Exists(backup.Path))
        {
            return this.BadRequest(new { success = false, message = "Backup not found." });
        }

        try
        {
            // Clear active SQLite connection pools to release file locks before file replacement
            SqliteConnection.ClearAllPools();

            // Before extracting restored files, cleanly delete existing stale WAL and shared memory files
            // so they do not conflict with the restored main database header salt.
            var walPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db-wal");
            var shmPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db-shm");

            if (global::System.IO.File.Exists(walPath))
            {
                global::System.IO.File.Delete(walPath);
            }

            if (global::System.IO.File.Exists(shmPath))
            {
                global::System.IO.File.Delete(shmPath);
            }

            using (var zip = ZipFile.OpenRead(backup.Path))
            {
                foreach (var entry in zip.Entries)
                {
                    var fileName = Path.GetFileName(entry.FullName);
                    if (string.Equals(fileName, "leecharr.db", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, "leecharr.db-wal", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, "config.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(this.appFolderInfo.AppDataFolder, fileName);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }
            }

            // Execute an integrity check on the restored SQLite database to verify validity
            var dbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
            if (global::System.IO.File.Exists(dbPath))
            {
                try
                {
                    using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                    {
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "PRAGMA integrity_check;";
                        var checkResult = cmd.ExecuteScalar()?.ToString();
                        if (string.Equals(checkResult, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            this.logger.Info("SQLite database integrity check passed for restored database at {0}", dbPath);
                        }
                        else
                        {
                            this.logger.Warn("SQLite database integrity check returned non-ok result for {0}: {1}", dbPath, checkResult);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to verify SQLite integrity check for {0}", dbPath);
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
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
