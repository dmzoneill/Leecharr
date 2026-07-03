using System;
using System.Data;

namespace NzbDrone.Core.Datastore;

public class Database : IDatabase
{
    private readonly Func<IDbConnection> _connectionFactory;

    public Database(Func<IDbConnection> connectionFactory, DatabaseType databaseType)
    {
        _connectionFactory = connectionFactory;
        DatabaseType = databaseType;
    }

    public DatabaseType DatabaseType { get; }

    public Version Version => new(1, 0);

    public IDbConnection OpenConnection()
    {
        var connection = _connectionFactory();
        connection.Open();
        return connection;
    }
}
