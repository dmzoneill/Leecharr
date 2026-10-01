// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Developer.Diagnostics;

public class ThreadDiagnosticItem
{
    public int ThreadId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Priority { get; set; } = string.Empty;

    public bool IsThreadPoolThread { get; set; }

    public bool IsAlive { get; set; }

    public string WaitReason { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("stackTrace")]
    public string CallStack { get; set; } = string.Empty;
}

public class MemoryDiagnosticReport
{
    public long TotalAllocatedBytes { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }

    public long HeapSizeBytes { get; set; }

    public long TotalMemoryBytes { get; set; }

    public long FragmentedBytes { get; set; }

    public int PinnedObjectsCount { get; set; }
}

public class EnvironmentDiagnosticReport
{
    public string OsDescription { get; set; } = string.Empty;

    public string FrameworkDescription { get; set; } = string.Empty;

    public string RuntimeIdentifier { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public int ProcessorCount { get; set; }

    public long WorkingSet64 { get; set; }

    public long NonPagedSystemMemorySize64 { get; set; }

    public long PagedMemorySize64 { get; set; }

    public long PeakWorkingSet64 { get; set; }

    public string SystemUptime { get; set; } = string.Empty;

    public string ProcessUptime { get; set; } = string.Empty;
}

public class GcCollectionRequest
{
    public int Generation { get; set; } = 2;

    public bool Compact { get; set; } = false;

    public bool Blocking { get; set; } = true;
}

public class GcCollectionResponse
{
    public bool Success { get; set; } = true;

    public string Message { get; set; } = string.Empty;

    public long MemoryFreedBytes { get; set; }

    public long DurationMs { get; set; }
}

public interface IDeveloperDiagnosticsService
{
    IReadOnlyList<ThreadDiagnosticItem> GetThreads();

    MemoryDiagnosticReport GetMemoryReport();

    EnvironmentDiagnosticReport GetEnvironmentReport();

    GcCollectionResponse ForceGarbageCollection(GcCollectionRequest request);
}
