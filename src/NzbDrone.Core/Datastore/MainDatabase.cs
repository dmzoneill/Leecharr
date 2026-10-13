// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Datastore;

public interface IMainDatabase : IDatabase
{
}

public class MainDatabase : IMainDatabase
{
    private const string DbFileName = "leecharr.db";
    private static readonly byte[] SqliteHeader = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");

    private readonly IDatabase database;
    private readonly Logger logger;

    public MainDatabase(IDbFactory dbFactory, IConnectionStringFactory connectionStringFactory, IAppFolderInfo appFolderInfo)
        : this(dbFactory, connectionStringFactory, appFolderInfo, null)
    {
    }

    internal MainDatabase(IDbFactory dbFactory, IConnectionStringFactory connectionStringFactory, IAppFolderInfo appFolderInfo, Action<string> deleteBackupFile)
    {
        this.logger = LogManager.GetCurrentClassLogger();

        if (connectionStringFactory.DatabaseType == DatabaseType.SQLite)
        {
            this.ApplyPendingRestore(appFolderInfo.AppDataFolder, deleteBackupFile ?? new Action<string>(File.Delete));
        }

        this.database = dbFactory.Create(
            connectionStringFactory.DatabaseType,
            connectionStringFactory.MainDbConnectionString);
    }

    public IDbConnection OpenConnection() => this.database.OpenConnection();

    public DatabaseType DatabaseType => this.database.DatabaseType;

    public Version Version => this.database.Version;

    private static void ValidateRestoreFile(string dbRestorePath)
    {
        var fileInfo = new FileInfo(dbRestorePath);
        if (fileInfo.Length < 100)
        {
            throw new InvalidOperationException($"Pending database restore file at '{dbRestorePath}' is too small ({fileInfo.Length} bytes) to be a valid SQLite database.");
        }

        using (var stream = File.OpenRead(dbRestorePath))
        {
            var header = new byte[SqliteHeader.Length];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < SqliteHeader.Length || !SqliteHeader.AsSpan().SequenceEqual(header))
            {
                throw new InvalidOperationException($"Pending database restore file at '{dbRestorePath}' has an invalid SQLite header.");
            }
        }

        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbRestorePath};Mode=ReadOnly;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                var result = cmd.ExecuteScalar()?.ToString();
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Integrity check failed for pending restore file '{dbRestorePath}': {result}");
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private void ApplyPendingRestore(string appDataFolder, Action<string> deleteBackupFile)
    {
        var dbPath = Path.Combine(appDataFolder, DbFileName);
        var dbRestorePath = dbPath + ".restore";

        if (!File.Exists(dbRestorePath))
        {
            return;
        }

        this.logger.Warn("Pending database restore found at {0}; applying before opening connections", dbRestorePath);

        var backupDbPath = dbPath + ".bak";
        var backupWalPath = dbPath + "-wal.bak";
        var backupShmPath = dbPath + "-shm.bak";
        var backupJournalPath = dbPath + "-journal.bak";

        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";
        var journalPath = dbPath + "-journal";

        var backedUpDb = false;
        var backedUpWal = false;
        var backedUpShm = false;
        var backedUpJournal = false;
        var restoreApplied = false;

        try
        {
            ValidateRestoreFile(dbRestorePath);

            SqliteConnection.ClearAllPools();

            if (File.Exists(dbPath))
            {
                File.Move(dbPath, backupDbPath, overwrite: true);
                backedUpDb = true;
            }

            if (File.Exists(walPath))
            {
                File.Move(walPath, backupWalPath, overwrite: true);
                backedUpWal = true;
            }

            if (File.Exists(shmPath))
            {
                File.Move(shmPath, backupShmPath, overwrite: true);
                backedUpShm = true;
            }

            if (File.Exists(journalPath))
            {
                File.Move(journalPath, backupJournalPath, overwrite: true);
                backedUpJournal = true;
            }

            File.Move(dbRestorePath, dbPath, overwrite: true);
            restoreApplied = true;

            if (backedUpDb && File.Exists(backupDbPath))
            {
                deleteBackupFile(backupDbPath);
            }

            if (backedUpWal && File.Exists(backupWalPath))
            {
                deleteBackupFile(backupWalPath);
            }

            if (backedUpShm && File.Exists(backupShmPath))
            {
                deleteBackupFile(backupShmPath);
            }

            if (backedUpJournal && File.Exists(backupJournalPath))
            {
                deleteBackupFile(backupJournalPath);
            }

            this.logger.Info("Database restore applied successfully from {0}", dbRestorePath);
        }
        catch (Exception ex) when (restoreApplied)
        {
            this.logger.Warn(ex, "Database restore from {0} was already applied, but removing backup files failed. The restored database was left in place.", dbRestorePath);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to apply pending database restore from {0}; rolling back to original database", dbRestorePath);

            try
            {
                if (backedUpDb && File.Exists(backupDbPath))
                {
                    File.Move(backupDbPath, dbPath, overwrite: true);
                }

                if (backedUpWal && File.Exists(backupWalPath))
                {
                    File.Move(backupWalPath, walPath, overwrite: true);
                }

                if (backedUpShm && File.Exists(backupShmPath))
                {
                    File.Move(backupShmPath, shmPath, overwrite: true);
                }

                if (backedUpJournal && File.Exists(backupJournalPath))
                {
                    File.Move(backupJournalPath, journalPath, overwrite: true);
                }
            }
            catch (Exception rollbackEx)
            {
                this.logger.Error(rollbackEx, "Failed to roll back original database files from backup");
                throw new InvalidOperationException(
                    $"Failed to roll back the original database after a failed restore. The database file may be missing at '{dbPath}' with a backup at '{backupDbPath}'. Startup is aborted to avoid creating an empty database.",
                    rollbackEx);
            }

            try
            {
                if (File.Exists(dbRestorePath))
                {
                    var failedPath = dbRestorePath + ".failed";
                    File.Move(dbRestorePath, failedPath, overwrite: true);
                    this.logger.Warn("Preserved failed restore file at {0}", failedPath);
                }
            }
            catch (Exception preserveEx)
            {
                this.logger.Warn(preserveEx, "Failed to rename failed restore file {0}", dbRestorePath);
            }

            if (!File.Exists(dbPath) && File.Exists(backupDbPath))
            {
                throw new InvalidOperationException(
                    $"Main database file is missing at '{dbPath}' but backup remains at '{backupDbPath}' after a failed restore. Startup is aborted to avoid creating an empty database.");
            }
        }
    }
}
