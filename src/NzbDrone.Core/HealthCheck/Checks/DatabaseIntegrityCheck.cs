// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DatabaseIntegrityCheck : IHealthCheck
{
    private const int ExpectedCoreTableCount = 3;

    private readonly IDatabase database;
    private readonly Logger logger;

    public DatabaseIntegrityCheck(IDatabase database = null)
    {
        this.database = database;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (this.database == null)
        {
            return Task.FromResult(HealthCheckResult.Error("Database", "Database is unavailable or not configured."));
        }

        try
        {
            using var conn = this.database.OpenConnection();
            if (conn == null)
            {
                return Task.FromResult(HealthCheckResult.Error("Database", "Database connection could not be opened."));
            }

            using var cmd = conn.CreateCommand();

            if (this.database.DatabaseType == DatabaseType.PostgreSQL)
            {
                return Task.FromResult(this.CheckPostgreSqlIntegrity(cmd));
            }

            cmd.CommandText = "PRAGMA quick_check;";
            var result = cmd.ExecuteScalar()?.ToString()?.Trim();

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(HealthCheckResult.Ok("Database"));
            }

            var detail = string.IsNullOrWhiteSpace(result)
                ? "Integrity check returned no results"
                : result;

            return Task.FromResult(HealthCheckResult.Error("Database", $"Database integrity check failed: {detail}"));
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Database integrity health check failed");
            return Task.FromResult(HealthCheckResult.Error("Database", $"Database integrity check failed: {ex.Message}"));
        }
    }

    private HealthCheckResult CheckPostgreSqlIntegrity(System.Data.IDbCommand cmd)
    {
        cmd.CommandText = @"
SELECT COUNT(*)
FROM pg_catalog.pg_class c
INNER JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind IN ('r', 'p')
  AND c.relname IN ('VersionInfo', 'Config', 'Torrents');";
        var coreTableCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (coreTableCount < ExpectedCoreTableCount)
        {
            return HealthCheckResult.Error(
                "Database",
                $"Database schema is incomplete: expected core Leecharr tables but found {coreTableCount}.");
        }

        cmd.CommandText = "SELECT COUNT(1) FROM \"VersionInfo\";";
        var migrationCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (migrationCount <= 0)
        {
            return HealthCheckResult.Error("Database", "Database migration history is empty.");
        }

        cmd.CommandText = "SELECT COUNT(1) FROM \"Config\";";
        cmd.ExecuteScalar();
        cmd.CommandText = "SELECT COUNT(1) FROM \"Torrents\";";
        cmd.ExecuteScalar();

        return HealthCheckResult.Ok("Database");
    }
}
