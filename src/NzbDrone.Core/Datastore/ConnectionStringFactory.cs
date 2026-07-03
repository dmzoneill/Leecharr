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
    private readonly IConfigFileProvider _configFileProvider;

    public ConnectionStringFactory(IAppFolderInfo appFolderInfo, IConfigFileProvider configFileProvider)
    {
        if (appFolderInfo == null)
        {
            throw new ArgumentNullException(nameof(appFolderInfo));
        }

        _configFileProvider = configFileProvider ?? throw new ArgumentNullException(nameof(configFileProvider));

        if (!string.IsNullOrEmpty(_configFileProvider.PostgresHost))
        {
            DatabaseType = DatabaseType.PostgreSQL;
            MainDbConnectionString = BuildPostgresConnectionString();
        }
        else
        {
            DatabaseType = DatabaseType.SQLite;
            MainDbConnectionString = BuildSqliteConnectionString(appFolderInfo.AppDataFolder);
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
        return $"Host={_configFileProvider.PostgresHost};" +
            $"Port={_configFileProvider.PostgresPort};" +
            $"Database={_configFileProvider.PostgresMainDb};" +
            $"Username={_configFileProvider.PostgresUser};" +
            $"Password={_configFileProvider.PostgresPassword}";
    }
}
