// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Data;

namespace NzbDrone.Core.Datastore;

public class Database : IDatabase
{
    private readonly Func<IDbConnection> connectionFactory;
    private readonly int busyTimeout;

    public Database(Func<IDbConnection> connectionFactory, DatabaseType databaseType, int busyTimeout = 5000)
    {
        this.connectionFactory = connectionFactory;
        this.DatabaseType = databaseType;
        this.busyTimeout = busyTimeout;
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
            cmd.CommandText = $"PRAGMA busy_timeout = {this.busyTimeout}; PRAGMA cache_size = -64000; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
        }

        return connection;
    }
}
