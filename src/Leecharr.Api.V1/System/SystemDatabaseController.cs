// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Datastore;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/database")]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3001:Review SQL queries for security vulnerabilities", Justification = "Admin SQL explorer intentionally executes user-supplied and schema introspection SQL queries.")]
public class SystemDatabaseController : Controller
{
    private static readonly Regex WritePattern = new(
        @"^\s*(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REPLACE|VACUUM|ATTACH|DETACH|REINDEX)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IMainDatabase mainDatabase;
    private readonly Logger logger;

    public SystemDatabaseController(IMainDatabase mainDatabase)
    {
        this.mainDatabase = mainDatabase;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    [HttpGet("tables")]
    public ActionResult<List<DatabaseTableResource>> GetTables()
    {
        using var connection = this.mainDatabase.OpenConnection();
        var tables = new List<DatabaseTableResource>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

        using var reader = cmd.ExecuteReader();
        var tableNames = new List<string>();
        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }

        reader.Close();

        foreach (var name in tableNames)
        {
            var rowCount = 0L;
            var columnCount = 0;

            try
            {
                using var countCmd = connection.CreateCommand();
                countCmd.CommandText = $"SELECT COUNT(*) FROM \"{EscapeIdentifier(name)}\";";
                rowCount = Convert.ToInt64(countCmd.ExecuteScalar());
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to count rows for table {0}", name);
            }

            try
            {
                using var colsCmd = connection.CreateCommand();
                colsCmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(name)}\");";
                using var colsReader = colsCmd.ExecuteReader();
                while (colsReader.Read())
                {
                    columnCount++;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to inspect columns for table {0}", name);
            }

            tables.Add(new DatabaseTableResource
            {
                Name = name,
                RowCount = rowCount,
                ColumnCount = columnCount
            });
        }

        return this.Ok(tables);
    }

    [HttpGet("schema")]
    public ActionResult<DatabaseSchemaResponse> GetSchema()
    {
        using var connection = this.mainDatabase.OpenConnection();
        var response = new DatabaseSchemaResponse();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

        using var reader = cmd.ExecuteReader();
        var tableNames = new List<string>();
        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }

        reader.Close();

        foreach (var tableName in tableNames)
        {
            var tableSchema = new DatabaseTableSchemaResource { Name = tableName };

            // 1. Columns
            try
            {
                using var colCmd = connection.CreateCommand();
                colCmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\");";
                using var colReader = colCmd.ExecuteReader();
                while (colReader.Read())
                {
                    tableSchema.Columns.Add(new DatabaseColumnResource
                    {
                        Cid = colReader.GetInt32(0),
                        Name = colReader.GetString(1),
                        Type = colReader.IsDBNull(2) ? "TEXT" : colReader.GetString(2),
                        NotNull = colReader.GetInt32(3) == 1,
                        DefaultValue = colReader.IsDBNull(4) ? null : colReader.GetString(4),
                        IsPrimaryKey = colReader.GetInt32(5) > 0
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read columns for {0}", tableName);
            }

            // 2. Foreign Keys
            try
            {
                using var fkCmd = connection.CreateCommand();
                fkCmd.CommandText = $"PRAGMA foreign_key_list(\"{EscapeIdentifier(tableName)}\");";
                using var fkReader = fkCmd.ExecuteReader();
                while (fkReader.Read())
                {
                    tableSchema.ForeignKeys.Add(new DatabaseForeignKeyResource
                    {
                        Id = fkReader.GetInt32(0),
                        ToTable = fkReader.GetString(2),
                        FromColumn = fkReader.GetString(3),
                        ToColumn = fkReader.IsDBNull(4) ? "Id" : fkReader.GetString(4),
                        OnUpdate = fkReader.IsDBNull(5) ? "NO ACTION" : fkReader.GetString(5),
                        OnDelete = fkReader.IsDBNull(6) ? "NO ACTION" : fkReader.GetString(6)
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read foreign keys for {0}", tableName);
            }

            // 3. Indexes
            try
            {
                using var idxCmd = connection.CreateCommand();
                idxCmd.CommandText = $"PRAGMA index_list(\"{EscapeIdentifier(tableName)}\");";
                using var idxReader = idxCmd.ExecuteReader();
                var indexList = new List<(string IndexName, bool Unique)>();
                while (idxReader.Read())
                {
                    var idxName = idxReader.GetString(1);
                    var isUnique = idxReader.GetInt32(2) == 1;
                    indexList.Add((idxName, isUnique));
                }

                idxReader.Close();

                foreach (var (idxName, isUnique) in indexList)
                {
                    using var infoCmd = connection.CreateCommand();
                    infoCmd.CommandText = $"PRAGMA index_info(\"{EscapeIdentifier(idxName)}\");";
                    using var infoReader = infoCmd.ExecuteReader();
                    var cols = new List<string>();
                    while (infoReader.Read())
                    {
                        cols.Add(infoReader.GetString(2));
                    }

                    tableSchema.Indexes.Add(new DatabaseIndexResource
                    {
                        Name = idxName,
                        Unique = isUnique,
                        Columns = cols
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read indexes for {0}", tableName);
            }

            // 4. Row Count
            try
            {
                using var countCmd = connection.CreateCommand();
                countCmd.CommandText = $"SELECT COUNT(*) FROM \"{EscapeIdentifier(tableName)}\";";
                tableSchema.RowCount = Convert.ToInt64(countCmd.ExecuteScalar());
            }
            catch
            {
                tableSchema.RowCount = 0;
            }

            response.Tables.Add(tableSchema);
        }

        response.MermaidErd = GenerateMermaidErd(response.Tables);
        return this.Ok(response);
    }

    [HttpPost("query")]
    public async Task<ActionResult<DatabaseQueryResult>> ExecuteQuery([FromBody] DatabaseQueryRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Query))
        {
            return this.BadRequest(new DatabaseQueryResult
            {
                Success = false,
                ErrorMessage = "Query cannot be empty."
            });
        }

        var trimmedQuery = request.Query.Trim();
        var sw = Stopwatch.StartNew();

        // Enforce safe mode if readOnly is true
        if (request.ReadOnly && WritePattern.IsMatch(trimmedQuery))
        {
            return this.BadRequest(new DatabaseQueryResult
            {
                Success = false,
                ErrorMessage = "Query contains write/mutation statements (INSERT, UPDATE, DELETE, DROP, etc.) but Safe Mode (Read-Only) is enabled.",
                ExecutionTimeMs = 0
            });
        }

        using var connection = this.mainDatabase.OpenConnection();

        try
        {
            if (request.ReadOnly)
            {
                using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA query_only = ON;";
                pragma.ExecuteNonQuery();
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = trimmedQuery;
            cmd.CommandTimeout = 30;

            // Check if query produces a result set (SELECT, PRAGMA, EXPLAIN, etc.)
            var isWrite = WritePattern.IsMatch(trimmedQuery);

            if (isWrite && !request.ReadOnly)
            {
                using var transaction = connection.BeginTransaction();
                cmd.Transaction = transaction;

                var affected = cmd.ExecuteNonQuery();
                transaction.Commit();
                sw.Stop();

                return this.Ok(new DatabaseQueryResult
                {
                    Success = true,
                    IsQuery = false,
                    RowsAffected = affected,
                    ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
                    Message = $"Query executed successfully. {affected} row(s) affected."
                });
            }

            using var reader = cmd.ExecuteReader();
            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<List<object>>();
            var maxRows = 1000;
            var truncated = false;

            while (reader.Read())
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new List<object>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        row.Add(null);
                    }
                    else
                    {
                        var val = reader.GetValue(i);
                        if (val is byte[] bytes)
                        {
                            row.Add($"[BLOB {bytes.Length} bytes]");
                        }
                        else
                        {
                            row.Add(val);
                        }
                    }
                }

                rows.Add(row);
            }

            sw.Stop();

            return this.Ok(new DatabaseQueryResult
            {
                Success = true,
                IsQuery = true,
                Columns = columns,
                Rows = rows,
                TotalRows = rows.Count,
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
                Message = truncated ? $"Showing first {maxRows} rows (truncated)." : $"Returned {rows.Count} row(s)."
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            this.logger.Warn(ex, "Error executing database query: {0}", trimmedQuery);
            return this.Ok(new DatabaseQueryResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
            });
        }
    }

    private static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("\"", "\"\"");
    }

    private static string GenerateMermaidErd(List<DatabaseTableSchemaResource> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        // Relationships
        var foreignKeySeen = new HashSet<string>();
        foreach (var table in tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                var relKey = $"{table.Name}->{fk.ToTable}:{fk.FromColumn}";
                if (foreignKeySeen.Add(relKey))
                {
                    var cleanToTable = CleanEntityName(fk.ToTable);
                    var cleanFromTable = CleanEntityName(table.Name);
                    sb.AppendLine($"    \"{cleanToTable}\" ||--o{{ \"{cleanFromTable}\" : \"{fk.FromColumn}\"");
                }
            }
        }

        // Table structures
        foreach (var table in tables)
        {
            var cleanTableName = CleanEntityName(table.Name);
            sb.AppendLine($"    \"{cleanTableName}\" {{");

            foreach (var col in table.Columns)
            {
                var cleanType = CleanType(col.Type);
                var cleanCol = CleanEntityName(col.Name);
                var pkTag = col.IsPrimaryKey ? " PK" : string.Empty;
                var fkTag = table.ForeignKeys.Any(f => string.Equals(f.FromColumn, col.Name, StringComparison.OrdinalIgnoreCase)) ? " FK" : string.Empty;

                sb.AppendLine($"        {cleanType} {cleanCol}{pkTag}{fkTag}");
            }

            sb.AppendLine("    }");
        }

        return sb.ToString();
    }

    private static string CleanEntityName(string name)
    {
        return Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
    }

    private static string CleanType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "string";
        }

        var t = type.Trim().ToUpperInvariant();
        if (t.StartsWith("INT")) return "int";
        if (t.StartsWith("BIGINT")) return "int64";
        if (t.StartsWith("TEXT") || t.StartsWith("VARCHAR") || t.StartsWith("CHAR")) return "string";
        if (t.StartsWith("BOOL")) return "bool";
        if (t.StartsWith("DATE") || t.StartsWith("TIME")) return "datetime";
        if (t.StartsWith("REAL") || t.StartsWith("FLOAT") || t.StartsWith("DOUBLE") || t.StartsWith("NUMERIC")) return "float";
        if (t.StartsWith("BLOB")) return "blob";

        return "string";
    }
}
