// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Npgsql;

namespace NzbDrone.Core.Datastore;

public interface IDbFactory
{
    IDatabase Create(DatabaseType dbType, string connectionString);
}

public class SqliteDoubleTypeHandler : SqlMapper.TypeHandler<double>
{
    public override void SetValue(IDbDataParameter parameter, double value)
    {
        if (parameter != null)
        {
            parameter.Value = value;
        }
    }

    public override double Parse(object value)
    {
        return Convert.ToDouble(value);
    }
}

public class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        if (parameter != null)
        {
            parameter.Value = value.ToString("HH:mm:ss");
        }
    }

    public override TimeOnly Parse(object value)
    {
        return TimeOnly.Parse((string)value);
    }
}

public class DbFactory : IDbFactory
{
    private static bool typeHandlersRegistered;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public IDatabase Create(DatabaseType dbType, string connectionString)
    {
        if (!typeHandlersRegistered)
        {
            SqlMapper.AddTypeHandler(new SqliteDoubleTypeHandler());
            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<int>>());
            typeHandlersRegistered = true;
        }

        this.logger.Info("Creating {0} database: {1}", dbType, RedactConnectionString(dbType, connectionString));

        if (dbType == DatabaseType.SQLite)
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
            cmd.ExecuteNonQuery();
        }

        this.RunMigrations(dbType, connectionString);

        Func<IDbConnection> factory = dbType switch
        {
            DatabaseType.PostgreSQL => () => new NpgsqlConnection(connectionString),
            _ => () => new SqliteConnection(connectionString),
        };

        return new Database(factory, dbType);
    }

    private static string RedactConnectionString(DatabaseType dbType, string connectionString)
    {
        if (dbType == DatabaseType.PostgreSQL)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"Host={builder.Host};Database={builder.Database}";
        }

        var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
        return $"Data Source={sqliteBuilder.DataSource}";
    }

    private void RunMigrations(DatabaseType dbType, string connectionString)
    {
        var services = new ServiceCollection();

        services.AddFluentMigratorCore()
            .ConfigureRunner(rb =>
            {
                if (dbType == DatabaseType.PostgreSQL)
                {
                    rb.AddPostgres();
                }
                else
                {
                    rb.AddSQLite();
                }

                rb.WithGlobalConnectionString(connectionString)
                    .ScanIn(Assembly.GetExecutingAssembly()).For.Migrations();
            })
            .AddLogging(lb => lb.AddFluentMigratorConsole());

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        this.logger.Info("Database migrations complete");
    }
}
