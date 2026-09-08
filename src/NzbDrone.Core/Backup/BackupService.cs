// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Common;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Backup;

public class BackupService : IBackupService, IExecute<BackupCommand>, IExecuteAsync<BackupCommand>
{
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IDiskProvider diskProvider;
    private readonly IConnectionStringFactory connectionStringFactory;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IConfigService configService;
    private readonly Logger logger;

    public BackupService(
        IAppFolderInfo appFolderInfo,
        IDiskProvider diskProvider = null,
        IConnectionStringFactory connectionStringFactory = null,
        IConfigFileProvider configFileProvider = null,
        IConfigService configService = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.connectionStringFactory = connectionStringFactory;
        this.configFileProvider = configFileProvider;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task ExecuteAsync(BackupCommand message, CancellationToken cancellationToken = default)
    {
        this.CreateBackup(message?.Type ?? "Scheduled");
        return Task.CompletedTask;
    }

    public void Execute(BackupCommand message)
    {
        this.CreateBackup(message?.Type ?? "Scheduled");
    }

    public BackupResult CreateBackup(string type = "Manual")
    {
        string tempDumpFile = null;
        try
        {
            var isPostgres = this.IsPostgreSql();
            var dbTypeStr = isPostgres ? "PostgreSQL" : "SQLite";
            var includesDb = false;

            var subDir = string.Equals(type, "Scheduled", StringComparison.OrdinalIgnoreCase) ? "scheduled" : "manual";
            var targetDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups", subDir);
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
                var dumpSuccess = this.RunPgDump(pgDumpExe, host, port, user, password, dbName, tempDumpFile);
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
            var backup = new BackupResult
            {
                Name = zipName,
                Path = zipPath,
                Type = type,
                Size = fi.Length,
                Time = fi.LastWriteTimeUtc,
                DatabaseType = dbTypeStr,
                IncludesDatabase = includesDb,
            };

            this.logger.Info("Created {0} backup archive at {1} ({2} bytes, Database: {3}, IncludesDb: {4})", type, zipPath, fi.Length, dbTypeStr, includesDb);

            var backupDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups");
            this.PruneOldBackups(
                backupDir,
                this.configService?.BackupRetentionMaxCount ?? 14,
                this.configService?.BackupRetentionDays ?? 28);

            return backup;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to create backup archive");
            throw;
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

    public int PruneOldBackups(string backupDirectory = null, int maxBackupsToKeep = 14, int maxAgeDays = 28)
    {
        backupDirectory ??= Path.Combine(this.appFolderInfo.AppDataFolder, "Backups");
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

    protected virtual string FindPgDumpExecutable() => CliProcessDiscovery.FindExecutable("pg_dump");

    private int GetBackupTimeoutSeconds()
    {
        var timeout = this.configService?.DatabaseBackupTimeoutSeconds ?? 600;
        return timeout > 0 ? timeout : 600;
    }

    protected virtual bool RunPgDump(string pgDumpExe, string host, int port, string user, string password, string dbName, string outputPath)
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
            var exited = proc.WaitForExit(timeoutSeconds * 1000);
            if (!exited)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to kill timed out pg_dump process");
                }

                this.logger.Error("pg_dump process timed out after {0} seconds", timeoutSeconds);
                return false;
            }

            var stderr = stderrTask.GetAwaiter().GetResult();
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
}
