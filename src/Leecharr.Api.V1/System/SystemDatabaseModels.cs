// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace Leecharr.Api.V1.System;

public class DatabaseTableResource
{
    public string Name { get; set; } = string.Empty;

    public long RowCount { get; set; }

    public int ColumnCount { get; set; }
}

public class DatabaseColumnResource
{
    public int Cid { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool NotNull { get; set; }

    public string DefaultValue { get; set; }

    public bool IsPrimaryKey { get; set; }
}

public class DatabaseForeignKeyResource
{
    public int Id { get; set; }

    public string FromColumn { get; set; } = string.Empty;

    public string ToTable { get; set; } = string.Empty;

    public string ToColumn { get; set; } = string.Empty;

    public string OnUpdate { get; set; } = "NO ACTION";

    public string OnDelete { get; set; } = "NO ACTION";
}

public class DatabaseIndexResource
{
    public string Name { get; set; } = string.Empty;

    public bool Unique { get; set; }

    public List<string> Columns { get; set; } = new();
}

public class DatabaseTableSchemaResource
{
    public string Name { get; set; } = string.Empty;

    public long RowCount { get; set; }

    public List<DatabaseColumnResource> Columns { get; set; } = new();

    public List<DatabaseForeignKeyResource> ForeignKeys { get; set; } = new();

    public List<DatabaseIndexResource> Indexes { get; set; } = new();
}

public class DatabaseSchemaResponse
{
    public List<DatabaseTableSchemaResource> Tables { get; set; } = new();

    public string MermaidErd { get; set; } = string.Empty;
}

public class DatabaseQueryRequest
{
    public string Query { get; set; } = string.Empty;

    public bool ReadOnly { get; set; } = true;
}

public class DatabaseQueryResult
{
    public bool Success { get; set; }

    public string ErrorMessage { get; set; }

    public double ExecutionTimeMs { get; set; }

    public bool IsQuery { get; set; }

    public List<string> Columns { get; set; } = new();

    public List<List<object>> Rows { get; set; } = new();

    public int TotalRows { get; set; }

    public int RowsAffected { get; set; }

    public string Message { get; set; }

    public List<QueryPlanNode> QueryPlan { get; set; } = new();
}

public class QueryPlanNode
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public string Detail { get; set; } = string.Empty;
}

public class DatabaseStorageItem
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = "table";

    public string TableName { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public long PageCount { get; set; }

    public long RowCount { get; set; }

    public double Percentage { get; set; }
}

public class DatabaseStorageResponse
{
    public long TotalSizeBytes { get; set; }

    public long PageSize { get; set; }

    public long PageCount { get; set; }

    public long FreeSizeBytes { get; set; }

    public List<DatabaseStorageItem> Items { get; set; } = new();
}

public class DatabaseDiagnosticsResponse
{
    public string DatabasePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public long PageSize { get; set; }

    public long PageCount { get; set; }

    public long FreelistCount { get; set; }

    public string JournalMode { get; set; } = string.Empty;

    public string Synchronous { get; set; } = string.Empty;

    public long CacheSize { get; set; }

    public string Encoding { get; set; } = string.Empty;

    public string IntegrityCheck { get; set; } = string.Empty;

    public long GcTotalMemoryBytes { get; set; }

    public int GcGen0Collections { get; set; }

    public int GcGen1Collections { get; set; }

    public int GcGen2Collections { get; set; }

    public int ThreadPoolAvailableWorkerThreads { get; set; }

    public int ThreadPoolAvailableCompletionPortThreads { get; set; }

    public int ThreadPoolMaxWorkerThreads { get; set; }

    public double ProcessUptimeSeconds { get; set; }

    public long WorkingSetBytes { get; set; }

    public string DotNetVersion { get; set; } = string.Empty;
}

