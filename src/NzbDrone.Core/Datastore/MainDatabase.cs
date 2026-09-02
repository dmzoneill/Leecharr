// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Data;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Datastore;

public interface IMainDatabase : IDatabase
{
}

public class MainDatabase : IMainDatabase
{
    private const string DbFileName = "leecharr.db";

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

    private void ApplyPendingRestore(string appDataFolder)
    {
        var dbPath = Path.Combine(appDataFolder, DbFileName);
        var dbRestorePath = dbPath + ".restore";

        if (!File.Exists(dbRestorePath))
        {
            return;
        }

        this.logger.Warn("Pending database restore found at {0}; applying before opening connections", dbRestorePath);

        try
        {
            File.Move(dbRestorePath, dbPath, overwrite: true);
            this.logger.Info("Database restore applied successfully from {0}", dbRestorePath);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to apply pending database restore from {0}; original database retained", dbRestorePath);

            try
            {
                File.Delete(dbRestorePath);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
