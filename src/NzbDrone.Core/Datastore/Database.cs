// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Data;

namespace NzbDrone.Core.Datastore;

public class Database : IDatabase
{
    private readonly Func<IDbConnection> connectionFactory;

    public Database(Func<IDbConnection> connectionFactory, DatabaseType databaseType)
    {
        this.connectionFactory = connectionFactory;
        this.DatabaseType = databaseType;
    }

    public DatabaseType DatabaseType { get; }

    public Version Version => new(1, 0);

    public IDbConnection OpenConnection()
    {
        var connection = this.connectionFactory();
        connection.Open();

        if (this.DatabaseType == DatabaseType.SQLite)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
            cmd.ExecuteNonQuery();
        }

        return connection;
    }
}
