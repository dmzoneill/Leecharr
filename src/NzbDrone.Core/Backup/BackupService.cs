// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Common;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Backup;

public class BackupCommand : Command
{
    public string Type { get; set; } = "Scheduled";
}

public interface IBackupService
{
    string CreateBackup(string type = "Manual");
}

public class BackupService : IBackupService, IExecute<BackupCommand>
{
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IDiskProvider diskProvider;
    private readonly IConnectionStringFactory connectionStringFactory;
    private readonly IConfigFileProvider configFileProvider;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public BackupService(
        IAppFolderInfo appFolderInfo,
        IDiskProvider diskProvider = null,
        IConnectionStringFactory connectionStringFactory = null,
        IConfigFileProvider configFileProvider = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.diskProvider = diskProvider;
        this.connectionStringFactory = connectionStringFactory;
        this.configFileProvider = configFileProvider;
    }

    public void Execute(BackupCommand message)
    {
        var type = string.IsNullOrWhiteSpace(message?.Type) ? "Scheduled" : message.Type;
        this.CreateBackup(type);
    }

    public string CreateBackup(string type = "Manual")
    {
        string tempDumpFile = null;
        try
        {
            var isPostgres = this.IsPostgreSql();
            var dbTypeStr = isPostgres ? "PostgreSQL" : "SQLite";
            var includesDb = false;

            var subfolder = string.Equals(type, "Manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "scheduled";
            var targetDir = Path.Combine(this.appFolderInfo.AppDataFolder, "Backups", subfolder);
            Directory.CreateDirectory(targetDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var zipName = $"Leecharr_backup_{timestamp}.zip";
            var zipPath = Path.Combine(targetDir, zipName);

            if (isPostgres)
            {
                var pgDumpExe = CliProcessDiscovery.FindExecutable("pg_dump");
                var host = this.configFileProvider?.PostgresHost;
                var port = this.configFileProvider?.PostgresPort ?? 5432;
                var user = this.configFileProvider?.PostgresUser;
                var password = this.configFileProvider?.PostgresPassword;
                var dbName = this.configFileProvider?.PostgresMainDb;

                if (!string.IsNullOrEmpty(pgDumpExe) && !string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(dbName))
                {
                    tempDumpFile = Path.Combine(Path.GetTempPath(), $"leecharr_postgres_{Guid.NewGuid():N}.sql");
                    var dumpSuccess = this.RunPgDump(pgDumpExe, host, port, user, password, dbName, tempDumpFile);
                    if (dumpSuccess)
                    {
                        includesDb = true;
                    }
                    else
                    {
                        this.logger.Warn("pg_dump execution failed or produced empty file; creating backup with config only.");
                    }
                }
                else
                {
                    this.logger.Warn("pg_dump executable not found or PostgreSQL parameters missing; creating config-only backup for PostgreSQL instance.");
                }
            }
            else
            {
                var dbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
                if (File.Exists(dbPath))
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
                    if (includesDb && tempDumpFile != null && File.Exists(tempDumpFile))
                    {
                        zip.CreateEntryFromFile(tempDumpFile, "leecharr_postgres.sql");
                    }
                }
                else
                {
                    var dbPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db");
                    var walPath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr.db-wal");

                    if (File.Exists(dbPath))
                    {
                        zip.CreateEntryFromFile(dbPath, "leecharr.db");
                    }

                    if (File.Exists(walPath))
                    {
                        zip.CreateEntryFromFile(walPath, "leecharr.db-wal");
                    }
                }

                var configPath = Path.Combine(this.appFolderInfo.AppDataFolder, "config.xml");
                if (File.Exists(configPath))
                {
                    zip.CreateEntryFromFile(configPath, "config.xml");
                }
            }

            var fi = new FileInfo(zipPath);
            this.logger.Info("Created {0} backup archive at {1} ({2} bytes, Database: {3}, IncludesDb: {4})", type, zipPath, fi.Length, dbTypeStr, includesDb);
            return zipPath;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to create backup archive");
            throw;
        }
        finally
        {
            if (tempDumpFile != null && File.Exists(tempDumpFile))
            {
                try
                {
                    File.Delete(tempDumpFile);
                }
                catch
                {
                    // Ignore temp file cleanup error
                }
            }
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

    private bool RunPgDump(string pgDumpExe, string host, int port, string user, string password, string dbName, string outputPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pgDumpExe,
                Arguments = $"--clean --if-exists -h " { host } " -p {port} -U " { user } " -d " { dbName } " -f " { outputPath } string.Empty,
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
                return false;
            }

            proc.WaitForExit(30000);
            return proc.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "pg_dump execution failed");
            return false;
        }
    }
}
