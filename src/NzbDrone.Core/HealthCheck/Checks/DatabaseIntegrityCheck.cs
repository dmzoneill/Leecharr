// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DatabaseIntegrityCheck : IHealthCheck
{
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
                cmd.CommandText = "SELECT 1;";
                cmd.ExecuteScalar();
                return Task.FromResult(HealthCheckResult.Ok("Database"));
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
            return Task.FromResult(HealthCheckResult.Error("Database", $"Database integrity health check failed: {ex.Message}"));
        }
    }
}
