// Copyright (c) FeedItOut. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
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
[Authorize(Policy = "RequireAdmin")]
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
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to read zip archive metadata for {Backup}", fi.Name);
            }

            list.Add(new BackupResource
            {
                Id = id++,
                Name = fi.Name,
                Path = fi.Name,
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
        if (backup == null)
        {
            return this.NotFound();
        }

        var physicalPath = this.ResolveBackupPhysicalPath(backup.Name);
        if (string.IsNullOrEmpty(physicalPath) || !global::System.IO.File.Exists(physicalPath))
        {
            return this.NotFound();
        }

        var stream = global::System.IO.File.OpenRead(physicalPath);
        return this.File(stream, "application/zip", backup.Name);
    }

    [HttpPost] /* csrf-safe: SameSite=Lax auth cookie, JSON/API-key endpoints */ // nosemgrep
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
                    var tempSnapshotPath = Path.Combine(Path.GetTempPath(), $"leecharr_backup_{Guid.NewGuid():N}.db");
                    try
                    {
                        using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
                        {
                            conn.Open();
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = "VACUUM INTO @snapshotPath;";
                            cmd.Parameters.AddWithValue("@snapshotPath", tempSnapshotPath);
                            cmd.ExecuteNonQuery();
                        }

                        tempDumpFile = tempSnapshotPath;
                        includesDb = true;
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to create atomic SQLite snapshot via VACUUM INTO on {0}", dbPath);
                        if (global::System.IO.File.Exists(tempSnapshotPath))
                        {
                            tempDumpFile = tempSnapshotPath;
                        }

                        throw new InvalidOperationException("SQLite backup failed: VACUUM INTO did not produce an atomic snapshot.", ex);
                    }
                    finally
                    {
                        SqliteConnection.ClearAllPools();
                    }
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
                else if (tempDumpFile != null && global::System.IO.File.Exists(tempDumpFile))
                {
                    zip.CreateEntryFromFile(tempDumpFile, "leecharr.db");
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
                Path = zipName,
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
            return this.StatusCode(500, new { success = false, message = "Failed to create backup archive." });
        }
        finally
        {
            if (tempDumpFile != null && global::System.IO.File.Exists(tempDumpFile))
            {
                try
                {
                    global::System.IO.File.Delete(tempDumpFile);
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to delete temporary dump file {TempFile}", tempDumpFile);
                }
            }
        }
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        var backups = this.GetBackupsInternal();
        var match = backups.FirstOrDefault(b => b.Id == id);
        if (match == null)
        {
            return this.NotFound();
        }

        var physicalPath = this.ResolveBackupPhysicalPath(match.Name);
        if (string.IsNullOrEmpty(physicalPath) || !global::System.IO.File.Exists(physicalPath))
        {
            return this.NotFound();
        }

        try
        {
            this.DeleteBackupArchive(physicalPath);
            this.logger.Info("Deleted backup archive: {0}", physicalPath);
            return this.Ok();
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to delete backup archive: {0}", physicalPath);
            return this.StatusCode(500, new { success = false, message = "Failed to delete backup archive." });
        }
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
            (!string.IsNullOrWhiteSpace(request.Path) && (string.Equals(b.Path, request.Path, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Name, Path.GetFileName(request.Path), StringComparison.OrdinalIgnoreCase))) ||
            (!string.IsNullOrWhiteSpace(request.FileName) && (string.Equals(b.Name, request.FileName, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(b.Path), request.FileName, StringComparison.OrdinalIgnoreCase))));

        var targetNameOrPath = backup?.Name ?? request.FileName ?? request.Path;
        var physicalPath = this.ResolveBackupPhysicalPath(targetNameOrPath);
        if (string.IsNullOrEmpty(physicalPath) || !global::System.IO.File.Exists(physicalPath))
        {
            return this.BadRequest(new { success = false, message = "Backup not found." });
        }

        var stagingDir = Path.Combine(Path.GetTempPath(), $"leecharr_restore_staging_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDir);

            try
            {
                using (var zip = ZipFile.OpenRead(physicalPath))
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
                this.logger.Error(ex, "Failed to extract backup archive {0}", physicalPath);
                return this.StatusCode(500, new { success = false, message = "Failed to extract backup archive." });
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

                            this.logger.Info("PostgreSQL database successfully restored via psql from backup {0}", physicalPath);
                            return this.Ok(new { success = true, message = "Backup and PostgreSQL database restored successfully. Please restart Leecharr." });
                        }
                        else
                        {
                            this.logger.Error("Failed to execute psql restore from backup {0}", physicalPath);
                            return this.StatusCode(500, new { success = false, message = "psql database restoration failed or timed out. Please inspect database logs." });
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(psqlExe))
                        {
                            this.logger.Error("psql executable not found on system PATH. Unable to restore PostgreSQL database from backup {0}", physicalPath);
                        }
                        else
                        {
                            this.logger.Error("PostgreSQL host or database name configuration is missing. Unable to restore PostgreSQL database from backup {0}", physicalPath);
                        }

                        return this.StatusCode(500, new { success = false, message = "PostgreSQL database restoration requires psql and valid PostgresHost/PostgresMainDb settings." });
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
                    // 1. If stagedWal exists in stagingDir (legacy archive format), checkpoint it into stagedDb before integrity check
                    if (global::System.IO.File.Exists(stagedWal) && new FileInfo(stagedWal).Length > 0)
                    {
                        if (!this.TryMergeStagedWalIntoDatabase(stagedDb, stagedWal, out var walMergeError))
                        {
                            this.logger.Error("Failed to merge staged WAL into database at {0}: {1}", stagedWal, walMergeError);
                            return this.StatusCode(500, new { success = false, message = walMergeError });
                        }
                    }

                    // 2. Verify SQLite integrity on the staged database before touching active database
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
                        return this.StatusCode(500, new { success = false, message = "Corrupted database in backup archive." });
                    }
                    finally
                    {
                        SqliteConnection.ClearAllPools();
                    }
                }

                var restoreDbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db.restore");
                var databaseStaged = false;

                // 3. Stage the verified restored database for safe startup replacement
                if (global::System.IO.File.Exists(stagedDb))
                {
                    global::System.IO.File.Copy(stagedDb, restoreDbPath, overwrite: true);
                    databaseStaged = true;
                }

                var hasConfigInArchive = global::System.IO.File.Exists(stagedConfig);

                // 4. Restore config.xml if present in backup archive
                if (hasConfigInArchive)
                {
                    var liveConfig = Path.Combine(this.appFolderInfo.AppDataFolder, "config.xml");
                    global::System.IO.File.Copy(stagedConfig, liveConfig, overwrite: true);
                }

                if (databaseStaged)
                {
                    this.logger.Info("Restored backup archive from {0}", physicalPath);
                    return this.Ok(new { success = true, message = "Backup restored successfully. Please restart Leecharr to apply database changes." });
                }

                this.RemovePendingSqliteDbRestore();

                if (global::System.IO.File.Exists(stagedPgSql))
                {
                    this.logger.Error("Backup {0} contains a PostgreSQL database dump but Leecharr is configured for SQLite", physicalPath);
                    return this.BadRequest(new { success = false, message = "Backup contains a PostgreSQL database dump but Leecharr is configured for SQLite." });
                }

                if (hasConfigInArchive)
                {
                    this.logger.Info("Restored config from backup archive {0} (no SQLite database in archive)", physicalPath);
                    return this.Ok(new { success = true, message = "Config restored successfully. (Backup did not contain a SQLite database). Please restart Leecharr." });
                }

                this.logger.Error("Backup archive {0} did not contain a SQLite database or config.xml", physicalPath);
                return this.BadRequest(new { success = false, message = "Backup archive did not contain a SQLite database or config.xml." });
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to restore backup archive from {0}", physicalPath);
            return this.StatusCode(500, new { success = false, message = "Failed to restore backup archive." });
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
    protected virtual bool TryMergeStagedWalIntoDatabase(string stagedDb, string stagedWal, out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            using (var conn = new SqliteConnection($"Data Source={stagedDb}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to checkpoint staged WAL into staged database: {0}", stagedWal);
            errorMessage = "Failed to merge WAL from backup into the SQLite database file.";
            return false;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }

        if (global::System.IO.File.Exists(stagedWal) && new FileInfo(stagedWal).Length > 0)
        {
            errorMessage = "WAL from backup could not be fully merged into the SQLite database file.";
            return false;
        }

        this.TryDeleteStagingSidecarFile(stagedWal);
        this.TryDeleteStagingSidecarFile(stagedDb + "-shm");
        return true;
    }

    [NonAction]
    private void TryDeleteStagingSidecarFile(string path)
    {
        if (!global::System.IO.File.Exists(path))
        {
            return;
        }

        try
        {
            global::System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to delete staging sidecar file {0}", path);
        }
    }

    [NonAction]
    private void RemovePendingSqliteDbRestore()
    {
        var restoreDbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db.restore");
        if (!global::System.IO.File.Exists(restoreDbPath))
        {
            return;
        }

        try
        {
            global::System.IO.File.Delete(restoreDbPath);
            this.logger.Info("Removed stale pending SQLite database restore at {0}", restoreDbPath);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to remove stale pending SQLite database restore at {0}", restoreDbPath);
        }
    }

    [NonAction]
    private string ResolveBackupPhysicalPath(string fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return null;
        }

        var backupDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups");
        if (!Directory.Exists(backupDir))
        {
            return null;
        }

        if (Path.IsPathRooted(fileNameOrPath))
        {
            var fullBackupDir = Path.GetFullPath(backupDir);
            var fullGivenPath = Path.GetFullPath(fileNameOrPath);
            if (fullGivenPath.StartsWith(fullBackupDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullGivenPath, fullBackupDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var s = global::System.IO.File.OpenRead(fullGivenPath);
                    return fullGivenPath;
                }
                catch
                {
                    // Not accessible
                }
            }
        }

        var fileName = Path.GetFileName(fileNameOrPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        // Match by exact file name only. Directory.GetFiles treats * and ? as wildcards.
        foreach (var candidatePath in Directory.EnumerateFiles(backupDir, "*.zip", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(candidatePath), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return candidatePath;
            }
        }

        return null;
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

    protected virtual void DeleteBackupArchive(string physicalPath)
    {
        global::System.IO.File.Delete(physicalPath);
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
            if (!PostgresCliArguments.TryBuildPgDumpArguments(host, port, user, dbName, outputPath, out var arguments))
            {
                this.logger.Error("Refusing to run pg_dump because a PostgreSQL connection setting is not safe to pass as an argument.");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = pgDumpExe, /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            if (!string.IsNullOrEmpty(password))
            {
                psi.EnvironmentVariables["PGPASSWORD"] = password;
            }

            using var proc = Process.Start(psi); /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
            if (proc == null)
            {
                this.logger.Error("Failed to start pg_dump process.");
                return false;
            }

            var stderrTask = proc.StandardError.ReadToEndAsync();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();

            var timeoutSeconds = this.GetBackupTimeoutSeconds();
            var waitTask = proc.WaitForExitAsync(CancellationToken.None);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None);

            var completedTask = await Task.WhenAny(waitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    using var reapCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await proc.WaitForExitAsync(reapCts.Token);
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
            if (!PostgresCliArguments.TryBuildPsqlArguments(host, port, user, dbName, sqlScriptPath, out var arguments))
            {
                this.logger.Error("Refusing to run psql because a PostgreSQL connection setting is not safe to pass as an argument.");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = psqlExe, /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

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
            var waitTask = proc.WaitForExitAsync(CancellationToken.None);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None);

            var completedTask = await Task.WhenAny(waitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    using var reapCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await proc.WaitForExitAsync(reapCts.Token);
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
