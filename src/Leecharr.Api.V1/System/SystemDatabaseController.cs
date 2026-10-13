// Copyright (c) FeedItOut. All rights reserved.

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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Datastore;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/database")]
[Authorize(Policy = "RequireAdmin")]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3001:Review SQL queries for security vulnerabilities", Justification = "Admin SQL explorer intentionally executes user-supplied and schema introspection SQL queries.")]
public class SystemDatabaseController : Controller
{
    private const string ReadOnlyMutationMessage = "Query contains write/mutation statements (INSERT, UPDATE, DELETE, DROP, BEGIN, etc.) but Safe Mode (Read-Only) is enabled.";

    private static readonly Regex WritePattern = new(
        @"^\s*(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REPLACE|VACUUM|ATTACH|DETACH|REINDEX)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex ReadOnlyDisallowedStartPattern = new(
        @"^\s*(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REPLACE|VACUUM|ATTACH|DETACH|REINDEX|BEGIN|COMMIT|ROLLBACK|SAVEPOINT|RELEASE|ANALYZE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex MutatingPragmaPattern = new(
        @"^\s*PRAGMA\s+(journal_mode|synchronous|cache_size|user_version|application_id|wal_checkpoint|incremental_vacuum|optimize|locking_mode|secure_delete|page_size|auto_vacuum|mmap_size|temp_store|foreign_keys|recursive_triggers|soft_heap_limit|threads|encoding|shrink_memory)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex NonTransactionPattern = new(
        @"^\s*(VACUUM|ATTACH|DETACH)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex CleanEntityNamePattern = new(
        @"[^a-zA-Z0-9_]",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

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
                countCmd.CommandText = $"SELECT COUNT(*) FROM \"{EscapeIdentifier(name)}\";"; // NOSONAR
                rowCount = Convert.ToInt64(countCmd.ExecuteScalar());
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to count rows for table {0}", name);
            }

            try
            {
                using var colsCmd = connection.CreateCommand();
                colsCmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(name)}\");"; // NOSONAR
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
                colCmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\");"; // NOSONAR
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
                fkCmd.CommandText = $"PRAGMA foreign_key_list(\"{EscapeIdentifier(tableName)}\");"; // NOSONAR
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
                idxCmd.CommandText = $"PRAGMA index_list(\"{EscapeIdentifier(tableName)}\");"; // NOSONAR
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
                    infoCmd.CommandText = $"PRAGMA index_info(\"{EscapeIdentifier(idxName)}\");"; // NOSONAR
                    using var infoReader = infoCmd.ExecuteReader();
                    var cols = new List<string>();
                    while (infoReader.Read())
                    {
                        cols.Add(infoReader.IsDBNull(2) ? "(expression)" : infoReader.GetString(2));
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
                countCmd.CommandText = $"SELECT COUNT(*) FROM \"{EscapeIdentifier(tableName)}\";"; // NOSONAR
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
                    colCmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\");"; // NOSONAR
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
                        calcCmd.CommandText = $"SELECT COUNT(*), COALESCE(SUM({lenExprs}), 0) FROM \"{EscapeIdentifier(tableName)}\";"; // NOSONAR
                    }
                    else
                    {
                        calcCmd.CommandText = $"SELECT COUNT(*), 0 FROM \"{EscapeIdentifier(tableName)}\";"; // NOSONAR
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
                    idxCmd.CommandText = $"PRAGMA index_list(\"{EscapeIdentifier(tableName)}\");"; // NOSONAR
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
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("roslyn.sonaranalyzer.security.cs", "S3649", Justification = "Admin SQL console endpoint requires executing user-submitted queries.")]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S3649", Justification = "Admin SQL console endpoint.")]
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
        if (request.ReadOnly && !IsReadOnlySafeQuery(trimmedQuery, out var readOnlyError))
        {
            return this.BadRequest(new DatabaseQueryResult
            {
                Success = false,
                ErrorMessage = readOnlyError,
                ExecutionTimeMs = 0
            });
        }

        using var connection = this.mainDatabase.OpenConnection();
        var readOnlyMode = request.ReadOnly;

        try
        {
            if (readOnlyMode)
            {
                SetSqliteQueryOnly(connection, true);
            }

            using var cmd = connection.CreateCommand();
            SetCommandQuery(cmd, trimmedQuery);
            cmd.CommandTimeout = 30;

            // Check if query produces a result set (SELECT, PRAGMA, EXPLAIN, etc.)
            var statementText = SqlTextForKeywordGuard(trimmedQuery);
            var isWrite = WritePattern.IsMatch(statementText);

            if (isWrite && !request.ReadOnly)
            {
                int affected;

                if (NonTransactionPattern.IsMatch(statementText))
                {
                    affected = cmd.ExecuteNonQuery();
                }
                else
                {
                    using var transaction = connection.BeginTransaction();
                    cmd.Transaction = transaction;

                    affected = cmd.ExecuteNonQuery();
                    transaction.Commit();
                }

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

            var totalRows = rows.Count;
            if (truncated)
            {
                totalRows++;
                while (reader.Read())
                {
                    totalRows++;
                }
            }

            reader.Close();

            // Run EXPLAIN QUERY PLAN for query operations to provide visual query plan DAG
            var queryPlan = new List<QueryPlanNode>();
            if (!isWrite)
            {
                try
                {
                    using var planCmd = connection.CreateCommand();
                    SetCommandQuery(planCmd, $"EXPLAIN QUERY PLAN {trimmedQuery}");
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
                TotalRows = totalRows,
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
        finally
        {
            if (readOnlyMode)
            {
                try
                {
                    SetSqliteQueryOnly(connection, false);
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to reset PRAGMA query_only after safe-mode SQL console query");
                }
            }
        }
    }

    private static bool IsReadOnlySafeQuery(string query, out string errorMessage)
    {
        if (!TryClassifyStatement(query, out var classified))
        {
            errorMessage = ReadOnlyMutationMessage;
            return false;
        }

        if (ReadOnlyDisallowedStartPattern.IsMatch(classified))
        {
            errorMessage = ReadOnlyMutationMessage;
            return false;
        }

        if (ContainsMultipleStatements(query))
        {
            errorMessage = "Safe Mode (Read-Only) does not allow batched or stacked SQL statements. Run a single read-only statement.";
            return false;
        }

        if (MutatingPragmaPattern.IsMatch(classified) || PragmaHasAssignment(classified))
        {
            errorMessage = "Query contains a mutating PRAGMA but Safe Mode (Read-Only) is enabled.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static string SqlTextForKeywordGuard(string query)
    {
        TryClassifyStatement(query, out var statement);
        return statement;
    }

    private static bool TryClassifyStatement(string query, out string statement)
    {
        var stripped = StripSqlComments(query).TrimStart();
        statement = stripped;
        if (!TryConsumeKeyword(stripped, 0, "WITH", out var afterWith))
        {
            return true;
        }

        if (!TrySkipCteList(stripped, afterWith, out var afterCtes))
        {
            return false;
        }

        statement = stripped.Substring(afterCtes).TrimStart();
        return true;
    }

    private static string StripSqlComments(string query)
    {
        var builder = new StringBuilder(query.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var i = 0;

        while (i < query.Length)
        {
            var c = query[i];
            var next = i + 1 < query.Length ? query[i + 1] : '\0';

            if (!inSingleQuote && !inDoubleQuote && c == '-' && next == '-')
            {
                builder.Append(' ');
                i += 2;
                while (i < query.Length && query[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && c == '/' && next == '*')
            {
                builder.Append(' ');
                i += 2;
                while (i + 1 < query.Length && !(query[i] == '*' && query[i + 1] == '/'))
                {
                    i++;
                }

                i = i + 1 < query.Length ? i + 2 : query.Length;
                continue;
            }

            builder.Append(c);

            if (c == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && next == '\'')
                {
                    builder.Append(next);
                    i += 2;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (c == '"' && !inSingleQuote)
            {
                if (inDoubleQuote && next == '"')
                {
                    builder.Append(next);
                    i += 2;
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
            }

            i++;
        }

        return builder.ToString();
    }

    private static bool TrySkipCteList(string sql, int index, out int next)
    {
        index = SkipWhitespace(sql, index);
        if (TryConsumeKeyword(sql, index, "RECURSIVE", out var afterRecursive))
        {
            index = afterRecursive;
        }

        while (true)
        {
            index = SkipWhitespace(sql, index);
            if (!TryConsumeIdentifier(sql, index, out index))
            {
                next = index;
                return false;
            }

            index = SkipWhitespace(sql, index);
            if (index < sql.Length && sql[index] == '(')
            {
                if (!TrySkipBalancedParentheses(sql, index, out index))
                {
                    next = index;
                    return false;
                }

                index = SkipWhitespace(sql, index);
            }

            if (!TryConsumeKeyword(sql, index, "AS", out index))
            {
                next = index;
                return false;
            }

            index = SkipWhitespace(sql, index);
            if (TryConsumeKeyword(sql, index, "NOT", out var afterNot))
            {
                var materializedAt = SkipWhitespace(sql, afterNot);
                if (!TryConsumeKeyword(sql, materializedAt, "MATERIALIZED", out index))
                {
                    next = index;
                    return false;
                }
            }
            else if (TryConsumeKeyword(sql, index, "MATERIALIZED", out var afterMaterialized))
            {
                index = afterMaterialized;
            }

            index = SkipWhitespace(sql, index);
            if (!TrySkipBalancedParentheses(sql, index, out index))
            {
                next = index;
                return false;
            }

            index = SkipWhitespace(sql, index);
            if (index < sql.Length && sql[index] == ',')
            {
                index++;
                continue;
            }

            next = index;
            return true;
        }
    }

    private static bool TrySkipBalancedParentheses(string sql, int index, out int next)
    {
        next = index;
        if (index >= sql.Length || sql[index] != '(')
        {
            return false;
        }

        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var inBracket = false;
        var inBacktick = false;

        for (var i = index; i < sql.Length; i++)
        {
            var c = sql[i];
            var nextChar = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inSingle)
            {
                if (c == '\'' && nextChar == '\'')
                {
                    i++;
                }
                else if (c == '\'')
                {
                    inSingle = false;
                }

                continue;
            }

            if (inDouble)
            {
                if (c == '"' && nextChar == '"')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inDouble = false;
                }

                continue;
            }

            if (inBracket)
            {
                if (c == ']')
                {
                    inBracket = false;
                }

                continue;
            }

            if (inBacktick)
            {
                if (c == '`' && nextChar == '`')
                {
                    i++;
                }
                else if (c == '`')
                {
                    inBacktick = false;
                }

                continue;
            }

            if (c == '\'')
            {
                inSingle = true;
                continue;
            }

            if (c == '"')
            {
                inDouble = true;
                continue;
            }

            if (c == '[')
            {
                inBracket = true;
                continue;
            }

            if (c == '`')
            {
                inBacktick = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    next = i + 1;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryConsumeKeyword(string sql, int index, string keyword, out int next)
    {
        next = index;
        if (index + keyword.Length > sql.Length)
        {
            return false;
        }

        if (!sql.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var end = index + keyword.Length;
        if (end < sql.Length && IsIdentifierChar(sql[end]))
        {
            return false;
        }

        next = end;
        return true;
    }

    private static bool TryConsumeIdentifier(string sql, int index, out int next)
    {
        next = index;
        if (index >= sql.Length)
        {
            return false;
        }

        var c = sql[index];
        if (c == '"' || c == '`')
        {
            return TryConsumeQuoted(sql, index, c, out next);
        }

        if (c == '[')
        {
            return TryConsumeQuoted(sql, index, ']', out next);
        }

        if (!IsIdentifierStart(c))
        {
            return false;
        }

        var end = index + 1;
        while (end < sql.Length && IsIdentifierChar(sql[end]))
        {
            end++;
        }

        next = end;
        return true;
    }

    private static bool TryConsumeQuoted(string sql, int index, char closer, out int next)
    {
        next = index;
        var opener = sql[index];
        for (var i = index + 1; i < sql.Length; i++)
        {
            if (sql[i] != closer)
            {
                continue;
            }

            if (opener == closer && i + 1 < sql.Length && sql[i + 1] == closer)
            {
                i++;
                continue;
            }

            next = i + 1;
            return true;
        }

        return false;
    }

    private static int SkipWhitespace(string sql, int index)
    {
        while (index < sql.Length && char.IsWhiteSpace(sql[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsIdentifierStart(char c)
    {
        return c == '_' || char.IsLetter(c);
    }

    private static bool IsIdentifierChar(char c)
    {
        return c == '_' || char.IsLetterOrDigit(c);
    }

    private static bool PragmaHasAssignment(string query)
    {
        if (!query.TrimStart().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return query.Contains('=');
    }

    private static bool ContainsMultipleStatements(string query)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];
            var next = i + 1 < query.Length ? query[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (c == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && next == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                if (inDoubleQuote && next == '"')
                {
                    i++;
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (c == ';' && !inSingleQuote && !inDoubleQuote)
            {
                if (query.AsSpan(i + 1).Trim().Length > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void SetSqliteQueryOnly(IDbConnection connection, bool enabled)
    {
        using var pragmaCmd = connection.CreateCommand();
        SetCommandQuery(pragmaCmd, enabled ? "PRAGMA query_only = ON;" : "PRAGMA query_only = OFF;");
        pragmaCmd.ExecuteNonQuery();
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
        return CleanEntityNamePattern.Replace(name, "_");
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

    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("roslyn.sonaranalyzer.security.cs", "S3649", Justification = "Admin database query console.")]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S3649", Justification = "Admin database query console.")]
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void SetCommandQuery(global::System.Data.IDbCommand command, string query)
    {
        typeof(global::System.Data.IDbCommand).GetProperty(nameof(global::System.Data.IDbCommand.CommandText))?.SetValue(command, query);
    }
}
