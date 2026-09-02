// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
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
        if (appFolderInfo == null)
        {
            throw new ArgumentNullException(nameof(appFolderInfo));
        }

        this.configFileProvider = configFileProvider ?? throw new ArgumentNullException(nameof(configFileProvider));

        if (!string.IsNullOrEmpty(this.configFileProvider.PostgresHost))
        {
            this.DatabaseType = DatabaseType.PostgreSQL;
            this.MainDbConnectionString = this.BuildPostgresConnectionString();
        }
        else
        {
            this.DatabaseType = DatabaseType.SQLite;
            this.MainDbConnectionString = this.BuildSqliteConnectionString(appFolderInfo.AppDataFolder);
        }
    }

    public string MainDbConnectionString { get; }

    public DatabaseType DatabaseType { get; }

    private string BuildSqliteConnectionString(string dataFolder)
    {
        var dbPath = Path.Combine(dataFolder, "leecharr.db");
        return $"Data Source={dbPath};Cache=Shared";
    }

    private string BuildPostgresConnectionString()
    {
        return $"Host={this.configFileProvider.PostgresHost};" +
            $"Port={this.configFileProvider.PostgresPort};" +
            $"Database={this.configFileProvider.PostgresMainDb};" +
            $"Username={this.configFileProvider.PostgresUser};" +
            $"Password={this.configFileProvider.PostgresPassword}";
    }
}
