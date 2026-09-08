// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Common;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace Leecharr.Api.V1.Backup;

public class BackupResource : RestResource
{
    public string Name { get; set; }

    public string Path { get; set; }

    public string Type { get; set; } = "Manual";

    public long Size { get; set; }

    public DateTime Time { get; set; } = DateTime.UtcNow;

    public string DatabaseType { get; set; } = "SQLite";

    public bool IncludesDatabase { get; set; } = true;
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
    private readonly IConnectionStringFactory connectionStringFactory;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IConfigService configService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public BackupController(
        IAppFolderInfo appFolderInfo,
        IDiskProvider diskProvider = null,
        IConnectionStringFactory connectionStringFactory = null,
        IConfigFileProvider configFileProvider = null,
        IConfigService configService = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.diskProvider = diskProvider;
        this.connectionStringFactory = connectionStringFactory;
        this.configFileProvider = configFileProvider;
        this.configService = configService;
    }

    private bool IsPostgreSql()
    {
        if (this.connectionStringFactory != null && this.connectionStringFactory.DatabaseType == DatabaseType.PostgreSQL)
        {
            return true;
        }

        if (this.configFileProvider != null && !string.IsNullOrWhiteSpace(this.configFileProvider.PostgresHost))
        {
            return true;
        }

        return false;
    }

    private List<BackupResource> GetBackupsInternal()
    {
        var backupDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups");
        var list = new List<BackupResource>();

        if (!Directory.Exists(backupDir))
        {
            return list;
        }

        var isPostgresConfig = this.IsPostgreSql();
        var files = Directory.GetFiles(backupDir, "*.zip", SearchOption.AllDirectories);
        var id = 1;
        foreach (var file in files.OrderByDescending(global::System.IO.File.GetLastWriteTimeUtc))
        {
            var fi = new FileInfo(file);
            var isManual = file.Contains("manual", StringComparison.OrdinalIgnoreCase);

            var dbType = isPostgresConfig ? "PostgreSQL" : "SQLite";
            var includesDb = false;

            try
            {
                using var zip = ZipFile.OpenRead(file);
                if (zip.Entries.Any(e => string.Equals(e.FullName, "leecharr_postgres.sql", StringComparison.OrdinalIgnoreCase)))
                {
                    dbType = "PostgreSQL";
                    includesDb = true;
                }
                else if (zip.Entries.Any(e => string.Equals(e.FullName, "leecharr.db", StringComparison.OrdinalIgnoreCase)))
                {
                    dbType = "SQLite";
                    includesDb = true;
                }
            }
            catch
            {
                // Ignore archive reading errors
            }

            list.Add(new BackupResource
            {
                Id = id++,
                Name = fi.Name,
                Path = fi.FullName,
                Size = fi.Length,
                Time = fi.LastWriteTimeUtc,
                Type = isManual ? "Manual" : "Scheduled",
                DatabaseType = dbType,
                IncludesDatabase = includesDb,
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
    public async Task<ActionResult<BackupResource>> Create()
    {
        string tempDumpFile = null;
        try
        {
            var isPostgres = this.IsPostgreSql();
            var dbTypeStr = isPostgres ? "PostgreSQL" : "SQLite";
            var includesDb = false;

            var targetDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups", "manual");
            Directory.CreateDirectory(targetDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var zipName = $"Leecharr_backup_{timestamp}.zip";
            var zipPath = Path.Combine(targetDir, zipName);

            if (isPostgres)
            {
                var pgDumpExe = this.FindPgDumpExecutable();
                var host = this.configFileProvider?.PostgresHost;
                var port = this.configFileProvider?.PostgresPort ?? 5432;
                var user = this.configFileProvider?.PostgresUser;
                var password = this.configFileProvider?.PostgresPassword;
                var dbName = this.configFileProvider?.PostgresMainDb;

                if (string.IsNullOrEmpty(pgDumpExe))
                {
                    this.logger.Error("pg_dump executable not found on system PATH. Unable to create PostgreSQL database backup.");
                    throw new FileNotFoundException("pg_dump executable was not found on system PATH. Unable to create PostgreSQL database backup.");
                }

                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(dbName))
                {
                    this.logger.Error("PostgreSQL host or database name configuration is missing.");
                    throw new InvalidOperationException("PostgreSQL host or database name configuration is missing.");
                }

                tempDumpFile = Path.Combine(Path.GetTempPath(), $"leecharr_postgres_{Guid.NewGuid():N}.sql");
                var dumpSuccess = await this.RunPgDump(pgDumpExe, host, port, user, password, dbName, tempDumpFile);
                if (!dumpSuccess)
                {
                    this.logger.Error("pg_dump execution failed or timed out during PostgreSQL backup.");
                    throw new InvalidOperationException("PostgreSQL backup failed: pg_dump execution failed or timed out.");
                }

                includesDb = true;
            }
            else
            {
                var dbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
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

                    includesDb = true;
                }
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                if (isPostgres)
                {
                    if (includesDb && tempDumpFile != null && global::System.IO.File.Exists(tempDumpFile))
                    {
                        zip.CreateEntryFromFile(tempDumpFile, "leecharr_postgres.sql");
                    }
                }
                else
                {
                    var dbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
                    var walPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db-wal");

                    if (global::System.IO.File.Exists(dbPath))
                    {
                        zip.CreateEntryFromFile(dbPath, "leecharr.db");
                    }

                    if (global::System.IO.File.Exists(walPath))
                    {
                        zip.CreateEntryFromFile(walPath, "leecharr.db-wal");
                    }
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
                DatabaseType = dbTypeStr,
                IncludesDatabase = includesDb,
            };

            this.logger.Info("Created manual backup archive at {0} ({1} bytes, Database: {2}, IncludesDb: {3})", zipPath, fi.Length, dbTypeStr, includesDb);

            var backupDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups");
            this.PruneOldBackups(
                backupDir,
                this.configService?.BackupRetentionMaxCount ?? 14,
                this.configService?.BackupRetentionDays ?? 28);

            return this.Ok(backup);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to create backup archive");
            return this.StatusCode(500, new { success = false, message = ex.Message });
        }
        finally
        {
            if (tempDumpFile != null && global::System.IO.File.Exists(tempDumpFile))
            {
                try
                {
                    global::System.IO.File.Delete(tempDumpFile);
                }
                catch
                {
                    // Ignore temp file cleanup error
                }
            }
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
    public async Task<ActionResult> Restore([FromBody] RestoreBackupRequest request)
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

        var stagingDir = Path.Combine(Path.GetTempPath(), $"leecharr_restore_staging_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDir);

            try
            {
                using (var zip = ZipFile.OpenRead(backup.Path))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var fileName = Path.GetFileName(entry.FullName);
                        if (string.Equals(fileName, "leecharr.db", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fileName, "leecharr.db-wal", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fileName, "config.xml", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fileName, "leecharr_postgres.sql", StringComparison.OrdinalIgnoreCase))
                        {
                            var destPath = Path.Combine(stagingDir, fileName);
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to extract backup archive {0}", backup.Path);
                return this.StatusCode(500, new { success = false, message = $"Failed to extract backup archive: {ex.Message}" });
            }

            var stagedDb = Path.Combine(stagingDir, "leecharr.db");
            var stagedWal = Path.Combine(stagingDir, "leecharr.db-wal");
            var stagedConfig = Path.Combine(stagingDir, "config.xml");
            var stagedPgSql = Path.Combine(stagingDir, "leecharr_postgres.sql");

            var isPostgres = this.IsPostgreSql();

            if (isPostgres)
            {
                if (global::System.IO.File.Exists(stagedPgSql))
                {
                    var psqlExe = this.FindPsqlExecutable();
                    var host = this.configFileProvider?.PostgresHost;
                    var port = this.configFileProvider?.PostgresPort ?? 5432;
                    var user = this.configFileProvider?.PostgresUser;
                    var password = this.configFileProvider?.PostgresPassword;
                    var dbName = this.configFileProvider?.PostgresMainDb;

                    if (!string.IsNullOrEmpty(psqlExe) && !string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(dbName))
                    {
                        var restoreSuccess = await this.RunPsqlRestore(psqlExe, host, port, user, password, dbName, stagedPgSql);
                        if (restoreSuccess)
                        {
                            if (global::System.IO.File.Exists(stagedConfig))
                            {
                                var destConfig = Path.Combine(this.appFolderInfo.AppDataFolder, "config.xml");
                                global::System.IO.File.Copy(stagedConfig, destConfig, overwrite: true);
                            }

                            this.logger.Info("PostgreSQL database successfully restored via psql from backup {0}", backup.Path);
                            return this.Ok(new { success = true, message = "Backup and PostgreSQL database restored successfully. Please restart Leecharr." });
                        }
                        else
                        {
                            this.logger.Error("Failed to execute psql restore from backup {0}", backup.Path);
                            return this.StatusCode(500, new { success = false, message = "psql database restoration failed or timed out. Please inspect database logs." });
                        }
                    }
                    else
                    {
                        var appDataDump = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr_postgres.sql");
                        global::System.IO.File.Copy(stagedPgSql, appDataDump, overwrite: true);
                        if (global::System.IO.File.Exists(stagedConfig))
                        {
                            var destConfig = Path.Combine(this.appFolderInfo.AppDataFolder, "config.xml");
                            global::System.IO.File.Copy(stagedConfig, destConfig, overwrite: true);
                        }

                        this.logger.Info("psql executable not found; PostgreSQL dump extracted to {0}", appDataDump);
                        return this.Ok(new { success = true, message = "Config restored. PostgreSQL database dump 'leecharr_postgres.sql' extracted to application folder; please restore via psql. Please restart Leecharr." });
                    }
                }

                if (global::System.IO.File.Exists(stagedConfig))
                {
                    var destConfig = Path.Combine(this.appFolderInfo.AppDataFolder, "config.xml");
                    global::System.IO.File.Copy(stagedConfig, destConfig, overwrite: true);
                }

                return this.Ok(new { success = true, message = "Config restored successfully. (Backup did not contain a PostgreSQL database dump). Please restart Leecharr." });
            }
            else
            {
                // SQLite restore workflow
                if (global::System.IO.File.Exists(stagedDb))
                {
                    // 1. Verify SQLite integrity on the staged database before touching active database
                    try
                    {
                        var fileInfo = new FileInfo(stagedDb);
                        if (fileInfo.Length == 0)
                        {
                            this.logger.Error("SQLite database file in backup is empty: {0}", stagedDb);
                            return this.StatusCode(500, new { success = false, message = "SQLite database in backup is empty and invalid." });
                        }

                        using (var conn = new SqliteConnection($"Data Source={stagedDb};Mode=ReadOnly"))
                        {
                            conn.Open();
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = "PRAGMA integrity_check;";
                            var checkResult = cmd.ExecuteScalar()?.ToString();
                            if (!string.Equals(checkResult, "ok", StringComparison.OrdinalIgnoreCase))
                            {
                                this.logger.Error("SQLite database integrity check failed for staged backup at {0}: {1}", stagedDb, checkResult);
                                return this.StatusCode(500, new { success = false, message = $"SQLite database integrity check failed: {checkResult}" });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Failed to verify SQLite integrity check for staged backup at {0}", stagedDb);
                        return this.StatusCode(500, new { success = false, message = $"Corrupted database in backup archive: {ex.Message}" });
                    }
                    finally
                    {
                        SqliteConnection.ClearAllPools();
                    }
                }

                // 2. Prepare pre-restore safety snapshot of active database and config for rollback
                var backupDir = Path.Combine(Path.GetTempPath(), $"leecharr_prerestore_bak_{Guid.NewGuid():N}");
                Directory.CreateDirectory(backupDir);

                var liveDb = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
                var liveWal = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db-wal");
                var liveShm = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db-shm");
                var liveConfig = Path.Combine(this.appFolderInfo.AppDataFolder, "config.xml");

                var bakDb = Path.Combine(backupDir, "leecharr.db");
                var bakWal = Path.Combine(backupDir, "leecharr.db-wal");
                var bakShm = Path.Combine(backupDir, "leecharr.db-shm");
                var bakConfig = Path.Combine(backupDir, "config.xml");

                try
                {
                    // Clear active SQLite connection pools to release file locks before file replacement
                    SqliteConnection.ClearAllPools();

                    if (global::System.IO.File.Exists(liveDb))
                    {
                        global::System.IO.File.Copy(liveDb, bakDb, overwrite: true);
                    }

                    if (global::System.IO.File.Exists(liveWal))
                    {
                        global::System.IO.File.Copy(liveWal, bakWal, overwrite: true);
                    }

                    if (global::System.IO.File.Exists(liveShm))
                    {
                        global::System.IO.File.Copy(liveShm, bakShm, overwrite: true);
                    }

                    if (global::System.IO.File.Exists(liveConfig))
                    {
                        global::System.IO.File.Copy(liveConfig, bakConfig, overwrite: true);
                    }

                    // 3. Atomically replace active files
                    // Delete existing stale WAL and SHM files so they do not conflict with restored database
                    if (global::System.IO.File.Exists(liveWal))
                    {
                        global::System.IO.File.Delete(liveWal);
                    }

                    if (global::System.IO.File.Exists(liveShm))
                    {
                        global::System.IO.File.Delete(liveShm);
                    }

                    if (global::System.IO.File.Exists(stagedDb))
                    {
                        global::System.IO.File.Copy(stagedDb, liveDb, overwrite: true);
                    }

                    if (global::System.IO.File.Exists(stagedWal) && new FileInfo(stagedWal).Length > 0)
                    {
                        global::System.IO.File.Copy(stagedWal, liveWal, overwrite: true);
                    }

                    if (global::System.IO.File.Exists(stagedConfig))
                    {
                        global::System.IO.File.Copy(stagedConfig, liveConfig, overwrite: true);
                    }
                }
                catch (Exception copyEx)
                {
                    this.logger.Error(copyEx, "Error replacing database files during restore; rolling back active files.");
                    try
                    {
                        SqliteConnection.ClearAllPools();
                        if (global::System.IO.File.Exists(bakDb))
                        {
                            global::System.IO.File.Copy(bakDb, liveDb, overwrite: true);
                        }
                        else if (global::System.IO.File.Exists(liveDb))
                        {
                            global::System.IO.File.Delete(liveDb);
                        }

                        if (global::System.IO.File.Exists(bakWal))
                        {
                            global::System.IO.File.Copy(bakWal, liveWal, overwrite: true);
                        }
                        else if (global::System.IO.File.Exists(liveWal))
                        {
                            global::System.IO.File.Delete(liveWal);
                        }

                        if (global::System.IO.File.Exists(bakShm))
                        {
                            global::System.IO.File.Copy(bakShm, liveShm, overwrite: true);
                        }
                        else if (global::System.IO.File.Exists(liveShm))
                        {
                            global::System.IO.File.Delete(liveShm);
                        }

                        if (global::System.IO.File.Exists(bakConfig))
                        {
                            global::System.IO.File.Copy(bakConfig, liveConfig, overwrite: true);
                        }
                        else if (global::System.IO.File.Exists(liveConfig))
                        {
                            global::System.IO.File.Delete(liveConfig);
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        this.logger.Fatal(rollbackEx, "Critical error during rollback of restored files.");
                    }

                    return this.StatusCode(500, new { success = false, message = $"Failed to restore database files: {copyEx.Message}" });
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                    try
                    {
                        if (Directory.Exists(backupDir))
                        {
                            Directory.Delete(backupDir, recursive: true);
                        }
                    }
                    catch
                    {
                        // Ignore temp cleanup error
                    }
                }

                this.logger.Info("Restored backup archive from {0}", backup.Path);
                return this.Ok(new { success = true, message = "Backup restored successfully. Please restart Leecharr." });
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to restore backup archive from {0}", backup.Path);
            return this.StatusCode(500, new { success = false, message = ex.Message });
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDir))
                {
                    Directory.Delete(stagingDir, recursive: true);
                }
            }
            catch
            {
                // Ignore temp staging cleanup error
            }
        }
    }

    [NonAction]
    public int PruneOldBackups(string backupDirectory, int maxBackupsToKeep = 14, int maxAgeDays = 28)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
        {
            return 0;
        }

        var effectiveMaxKeep = maxBackupsToKeep > 0 ? maxBackupsToKeep : (this.configService?.BackupRetentionMaxCount ?? 14);
        var effectiveMaxAge = maxAgeDays > 0 ? maxAgeDays : (this.configService?.BackupRetentionDays ?? 28);
        var deletedCount = 0;

        try
        {
            var subDirs = Directory.GetDirectories(backupDirectory, "*", SearchOption.AllDirectories);
            foreach (var subDir in subDirs)
            {
                deletedCount += this.PruneDirectoryBackups(subDir, effectiveMaxKeep, effectiveMaxAge);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to inspect subdirectories in backup directory {0}", backupDirectory);
        }

        deletedCount += this.PruneDirectoryBackups(backupDirectory, effectiveMaxKeep, effectiveMaxAge);
        return deletedCount;
    }

    private int PruneDirectoryBackups(string directory, int maxBackupsToKeep, int maxAgeDays)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            var files = Directory.GetFiles(directory, "*.zip", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                return 0;
            }

            var cutoffDate = maxAgeDays > 0 ? DateTime.UtcNow.AddDays(-maxAgeDays) : DateTime.MinValue;

            var orderedFiles = files
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            var toDelete = new List<FileInfo>();

            for (var i = 0; i < orderedFiles.Count; i++)
            {
                var fi = orderedFiles[i];

                // Never delete the single newest backup to prevent leaving 0 backups
                if (i == 0)
                {
                    continue;
                }

                if (maxBackupsToKeep > 0 && i >= maxBackupsToKeep)
                {
                    toDelete.Add(fi);
                }
                else if (maxAgeDays > 0 && fi.LastWriteTimeUtc < cutoffDate)
                {
                    toDelete.Add(fi);
                }
            }

            var deleted = 0;
            foreach (var file in toDelete)
            {
                try
                {
                    if (file.Exists)
                    {
                        file.Delete();
                        this.logger.Info("Pruned old backup archive: {0}", file.FullName);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to delete old backup archive: {0}", file.FullName);
                }
            }

            return deleted;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to prune backup directory {0}", directory);
            return 0;
        }
    }

    protected virtual string FindPgDumpExecutable() => CliProcessDiscovery.FindExecutable("pg_dump");

    protected virtual string FindPsqlExecutable() => CliProcessDiscovery.FindExecutable("psql");

    private int GetBackupTimeoutSeconds()
    {
        var timeout = this.configService?.DatabaseBackupTimeoutSeconds ?? 600;
        return timeout > 0 ? timeout : 600;
    }

    private int GetRestoreTimeoutSeconds()
    {
        var timeout = this.configService?.DatabaseRestoreTimeoutSeconds ?? 600;
        return timeout > 0 ? timeout : 600;
    }

    protected virtual async Task<bool> RunPgDump(string pgDumpExe, string host, int port, string user, string password, string dbName, string outputPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pgDumpExe,
                Arguments = $"--clean --if-exists -h \"{host}\" -p {port} -U \"{user}\" -d \"{dbName}\" -f \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (!string.IsNullOrEmpty(password))
            {
                psi.EnvironmentVariables["PGPASSWORD"] = password;
            }

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                this.logger.Error("Failed to start pg_dump process.");
                return false;
            }

            var stderrTask = proc.StandardError.ReadToEndAsync();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();

            var timeoutSeconds = this.GetBackupTimeoutSeconds();
            var waitTask = proc.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

            var completedTask = await Task.WhenAny(waitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to kill timed out pg_dump process");
                }

                this.logger.Error("pg_dump process timed out after {0} seconds", timeoutSeconds);
                return false;
            }

            await Task.WhenAll(stderrTask, stdoutTask);
            var stderr = await stderrTask;

            var exitCode = -1;
            try
            {
                if (proc.HasExited)
                {
                    exitCode = proc.ExitCode;
                }
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }

            if (exitCode != 0)
            {
                this.logger.Error("pg_dump failed with exit code {0}: {1}", exitCode, stderr);
                return false;
            }

            return global::System.IO.File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "pg_dump execution threw an exception");
            return false;
        }
    }

    protected virtual async Task<bool> RunPsqlRestore(string psqlExe, string host, int port, string user, string password, string dbName, string sqlScriptPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = psqlExe,
                Arguments = $"-h \"{host}\" -p {port} -U \"{user}\" -d \"{dbName}\" -f \"{sqlScriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (!string.IsNullOrEmpty(password))
            {
                psi.EnvironmentVariables["PGPASSWORD"] = password;
            }

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                this.logger.Error("Failed to start psql process.");
                return false;
            }

            var stderrTask = proc.StandardError.ReadToEndAsync();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();

            var timeoutSeconds = this.GetRestoreTimeoutSeconds();
            var waitTask = proc.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

            var completedTask = await Task.WhenAny(waitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to kill timed out psql process");
                }

                this.logger.Error("psql restore process timed out after {0} seconds", timeoutSeconds);
                return false;
            }

            await Task.WhenAll(stderrTask, stdoutTask);
            var stderr = await stderrTask;

            var exitCode = -1;
            try
            {
                if (proc.HasExited)
                {
                    exitCode = proc.ExitCode;
                }
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }

            if (exitCode != 0)
            {
                this.logger.Error("psql restore failed with exit code {0}: {1}", exitCode, stderr);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "psql restore execution threw an exception");
            return false;
        }
    }
}
