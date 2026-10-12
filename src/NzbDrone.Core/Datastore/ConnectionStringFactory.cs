// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using Npgsql;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Datastore;

public interface IConnectionStringFactory
{
    string MainDbConnectionString { get; }

    DatabaseType DatabaseType { get; }
}

public class ConnectionStringFactory : IConnectionStringFactory
{
    private readonly IConfigFileProvider configFileProvider;

    public ConnectionStringFactory(IAppFolderInfo appFolderInfo, IConfigFileProvider configFileProvider)
    {
        ArgumentNullException.ThrowIfNull(appFolderInfo);

        this.configFileProvider = configFileProvider ?? throw new ArgumentNullException(nameof(configFileProvider));

        if (!string.IsNullOrEmpty(this.configFileProvider.PostgresHost))
        {
            this.DatabaseType = DatabaseType.PostgreSQL;
            this.MainDbConnectionString = this.BuildPostgresConnectionString();
        }
        else
        {
            this.DatabaseType = DatabaseType.SQLite;
            this.MainDbConnectionString = BuildSqliteConnectionString(appFolderInfo.AppDataFolder);
        }
    }

    public string MainDbConnectionString { get; }

    public DatabaseType DatabaseType { get; }

    private static string BuildSqliteConnectionString(string dataFolder)
    {
        var dbPath = Path.Combine(dataFolder, "leecharr.db");
        return $"Data Source={dbPath};Foreign Keys=True;Default Timeout=30;";
    }

    private string BuildPostgresConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = this.configFileProvider.PostgresHost,
            Port = this.configFileProvider.PostgresPort,
            Database = this.configFileProvider.PostgresMainDb,
            Username = this.configFileProvider.PostgresUser,
            Password = this.configFileProvider.PostgresPassword,
            Pooling = true,
            MinPoolSize = 1,
            MaxPoolSize = 50,
            Timeout = 15,
            CommandTimeout = 30,
        };

        return builder.ConnectionString;
    }
}
