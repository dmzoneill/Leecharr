// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Failed to read row count for table {0}", tableName);
                tableSchema.RowCount = 0;
            }

            response.Tables.Add(tableSchema);
        }

        response.MermaidErd = GenerateMermaidErd(response.Tables);
        return this.Ok(response);
    }

    [HttpGet("storage")]
    public ActionResult<DatabaseStorageResponse> GetStorage()
    {
        using var connection = this.mainDatabase.OpenConnection();
        var response = new DatabaseStorageResponse();

        long pageSize = 4096;
        long pageCount = 0;
        long freelistCount = 0;

        try
        {
            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA page_size;";
            pageSize = Convert.ToInt64(pragmaCmd.ExecuteScalar());

            pragmaCmd.CommandText = "PRAGMA page_count;";
            pageCount = Convert.ToInt64(pragmaCmd.ExecuteScalar());

            pragmaCmd.CommandText = "PRAGMA freelist_count;";
            freelistCount = Convert.ToInt64(pragmaCmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to fetch SQLite page info PRAGMAs");
        }

        response.PageSize = pageSize;
        response.PageCount = pageCount;
        response.TotalSizeBytes = pageSize * pageCount;
        response.FreeSizeBytes = pageSize * freelistCount;

        var items = new List<DatabaseStorageItem>();
        var dbstatSucceeded = false;

        try
        {
            using var dbstatCmd = connection.CreateCommand();
            dbstatCmd.CommandText = "SELECT name, sum(pgsize) as total_bytes, count(*) as pages FROM dbstat GROUP BY name ORDER BY total_bytes DESC;";
            using var reader = dbstatCmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var bytes = reader.GetInt64(1);
                var pages = reader.GetInt64(2);
                items.Add(new DatabaseStorageItem
                {
                    Name = name,
                    Type = name.StartsWith("sqlite_autoindex_") ? "index" : "table",
                    TableName = name,
                    Bytes = bytes,
                    PageCount = pages,
                    Percentage = response.TotalSizeBytes > 0 ? Math.Round((double)bytes / response.TotalSizeBytes * 100, 2) : 0
                });
            }

            dbstatSucceeded = true;
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to read dbstat table for storage estimation");
            dbstatSucceeded = false;
        }

        if (!dbstatSucceeded)
        {
            using var tablesCmd = connection.CreateCommand();
            tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            var tableNames = new List<string>();
            using (var reader = tablesCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            foreach (var tableName in tableNames)
            {
                long rowCount = 0;
                long payloadBytes = 0;
                var columns = new List<string>();

                try
                {
                    using var colCmd = connection.CreateCommand();
                    colCmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\");";
                    using var colReader = colCmd.ExecuteReader();
                    while (colReader.Read())
                    {
                        columns.Add(colReader.GetString(1));
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to read columns for storage estimation: {0}", tableName);
                }

                try
                {
                    using var calcCmd = connection.CreateCommand();
                    if (columns.Count > 0)
                    {
                        var lenExprs = string.Join(" + ", columns.Select(c => $"COALESCE(LENGTH(\"{EscapeIdentifier(c)}\"), 0)"));
                        calcCmd.CommandText = $"SELECT COUNT(*), COALESCE(SUM({lenExprs}), 0) FROM \"{EscapeIdentifier(tableName)}\";";
                    }
                    else
                    {
                        calcCmd.CommandText = $"SELECT COUNT(*), 0 FROM \"{EscapeIdentifier(tableName)}\";";
                    }

                    using var calcReader = calcCmd.ExecuteReader();
                    if (calcReader.Read())
                    {
                        rowCount = calcReader.GetInt64(0);
                        payloadBytes = calcReader.GetInt64(1);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to estimate size for table: {0}", tableName);
                }

                var estimatedRecordOverhead = rowCount * (8 + (columns.Count * 2));
                var totalTableBytes = payloadBytes + estimatedRecordOverhead;
                var pages = (long)Math.Ceiling((double)Math.Max(totalTableBytes, rowCount > 0 ? pageSize : 0) / pageSize);

                items.Add(new DatabaseStorageItem
                {
                    Name = tableName,
                    Type = "table",
                    TableName = tableName,
                    Bytes = totalTableBytes,
                    RowCount = rowCount,
                    PageCount = pages,
                    Percentage = response.TotalSizeBytes > 0 ? Math.Round((double)totalTableBytes / response.TotalSizeBytes * 100, 2) : 0
                });

                try
                {
                    using var idxCmd = connection.CreateCommand();
                    idxCmd.CommandText = $"PRAGMA index_list(\"{EscapeIdentifier(tableName)}\");";
                    using var idxReader = idxCmd.ExecuteReader();
                    var indexNames = new List<string>();
                    while (idxReader.Read())
                    {
                        indexNames.Add(idxReader.GetString(1));
                    }

                    foreach (var idxName in indexNames)
                    {
                        var idxBytes = rowCount * 16;
                        var idxPages = (long)Math.Ceiling((double)Math.Max(idxBytes, rowCount > 0 ? pageSize : 0) / pageSize);
                        items.Add(new DatabaseStorageItem
                        {
                            Name = idxName,
                            Type = "index",
                            TableName = tableName,
                            Bytes = idxBytes,
                            RowCount = rowCount,
                            PageCount = idxPages,
                            Percentage = response.TotalSizeBytes > 0 ? Math.Round((double)idxBytes / response.TotalSizeBytes * 100, 2) : 0
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to inspect indexes for storage: {0}", tableName);
                }
            }
        }

        if (response.FreeSizeBytes > 0)
        {
            items.Add(new DatabaseStorageItem
            {
                Name = "[Free Space]",
                Type = "free",
                TableName = "[Free Space]",
                Bytes = response.FreeSizeBytes,
                PageCount = freelistCount,
                RowCount = 0,
                Percentage = response.TotalSizeBytes > 0 ? Math.Round((double)response.FreeSizeBytes / response.TotalSizeBytes * 100, 2) : 0
            });
        }

        response.Items = items.OrderByDescending(i => i.Bytes).ToList();
        return this.Ok(response);
    }

    [HttpGet("diagnostics")]
    public ActionResult<DatabaseDiagnosticsResponse> GetDiagnostics()
    {
        using var connection = this.mainDatabase.OpenConnection();
        var diag = new DatabaseDiagnosticsResponse();

        try
        {
            using var cmd = connection.CreateCommand();

            cmd.CommandText = "PRAGMA database_list;";
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    diag.DatabasePath = reader.FieldCount > 2 ? reader.GetString(2) : string.Empty;
                }
            }

            if (!string.IsNullOrEmpty(diag.DatabasePath) && global::System.IO.File.Exists(diag.DatabasePath))
            {
                diag.FileSizeBytes = new global::System.IO.FileInfo(diag.DatabasePath).Length;
            }

            cmd.CommandText = "PRAGMA page_size;";
            diag.PageSize = Convert.ToInt64(cmd.ExecuteScalar());

            cmd.CommandText = "PRAGMA page_count;";
            diag.PageCount = Convert.ToInt64(cmd.ExecuteScalar());

            cmd.CommandText = "PRAGMA freelist_count;";
            diag.FreelistCount = Convert.ToInt64(cmd.ExecuteScalar());

            cmd.CommandText = "PRAGMA journal_mode;";
            diag.JournalMode = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;

            cmd.CommandText = "PRAGMA synchronous;";
            var syncVal = Convert.ToInt32(cmd.ExecuteScalar());
            diag.Synchronous = syncVal switch
            {
                0 => "OFF (0)",
                1 => "NORMAL (1)",
                2 => "FULL (2)",
                3 => "EXTRA (3)",
                _ => syncVal.ToString()
            };

            cmd.CommandText = "PRAGMA cache_size;";
            diag.CacheSize = Convert.ToInt64(cmd.ExecuteScalar());

            cmd.CommandText = "PRAGMA encoding;";
            diag.Encoding = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;

            cmd.CommandText = "PRAGMA quick_check(1);";
            diag.IntegrityCheck = Convert.ToString(cmd.ExecuteScalar()) ?? "ok";
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to read SQLite diagnostics PRAGMAs");
        }

        try
        {
            diag.GcTotalMemoryBytes = GC.GetTotalMemory(false);
            diag.GcGen0Collections = GC.CollectionCount(0);
            diag.GcGen1Collections = GC.CollectionCount(1);
            diag.GcGen2Collections = GC.CollectionCount(2);

            ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out _);
            diag.ThreadPoolAvailableWorkerThreads = workerThreads;
            diag.ThreadPoolAvailableCompletionPortThreads = completionPortThreads;
            diag.ThreadPoolMaxWorkerThreads = maxWorkerThreads;

            using var process = Process.GetCurrentProcess();
            diag.WorkingSetBytes = process.WorkingSet64;
            diag.ProcessUptimeSeconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds;
            diag.DotNetVersion = Environment.Version.ToString();
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to gather CLR diagnostics");
        }

        return this.Ok(diag);
    }

    [HttpPost("query")]
    public ActionResult<DatabaseQueryResult> ExecuteQuery([FromBody] DatabaseQueryRequest request)
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

            reader.Close();

            // Run EXPLAIN QUERY PLAN for query operations to provide visual query plan DAG
            var queryPlan = new List<QueryPlanNode>();
            if (!isWrite)
            {
                try
                {
                    using var planCmd = connection.CreateCommand();
                    planCmd.CommandText = $"EXPLAIN QUERY PLAN {trimmedQuery}";
                    planCmd.CommandTimeout = 10;
                    using var planReader = planCmd.ExecuteReader();
                    while (planReader.Read())
                    {
                        var planId = planReader.GetInt32(0);
                        var parentId = planReader.GetInt32(1);
                        var detail = planReader.IsDBNull(3) ? string.Empty : planReader.GetString(3);
                        queryPlan.Add(new QueryPlanNode
                        {
                            Id = planId,
                            ParentId = parentId,
                            Detail = detail
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Could not generate EXPLAIN QUERY PLAN for query: {0}", trimmedQuery);
                }
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
                Message = truncated ? $"Showing first {maxRows} rows (truncated)." : $"Returned {rows.Count} row(s).",
                QueryPlan = queryPlan
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
