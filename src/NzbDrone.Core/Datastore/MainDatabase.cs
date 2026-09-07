// Copyright (c) PlaceholderCompany. All rights reserved.

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
    {
        this.logger = LogManager.GetCurrentClassLogger();

        if (connectionStringFactory.DatabaseType == DatabaseType.SQLite)
        {
            this.ApplyPendingRestore(appFolderInfo.AppDataFolder);
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
                cmd.CommandText = "PRAGMA quick_check(1);";
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

    private void ApplyPendingRestore(string appDataFolder)
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

        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";

        var backedUpDb = false;
        var backedUpWal = false;
        var backedUpShm = false;

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

            File.Move(dbRestorePath, dbPath, overwrite: true);

            if (backedUpDb && File.Exists(backupDbPath))
            {
                File.Delete(backupDbPath);
            }

            if (backedUpWal && File.Exists(backupWalPath))
            {
                File.Delete(backupWalPath);
            }

            if (backedUpShm && File.Exists(backupShmPath))
            {
                File.Delete(backupShmPath);
            }

            this.logger.Info("Database restore applied successfully from {0}", dbRestorePath);
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
            }
            catch (Exception rollbackEx)
            {
                this.logger.Error(rollbackEx, "Failed to roll back original database files from backup");
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
        }
    }
}
