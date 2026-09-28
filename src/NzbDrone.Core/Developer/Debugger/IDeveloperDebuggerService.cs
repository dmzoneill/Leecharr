// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Developer.Debugger;

public class TracepointDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string FilePath { get; set; } = string.Empty;

    public int LineNumber { get; set; }

    public string Condition { get; set; } = string.Empty;

    public int HitCount { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class TracepointSnapshot
{
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");

    public string TracepointId { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public int LineNumber { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public int ThreadId { get; set; }

    public string CallStack { get; set; } = string.Empty;

    public string VariablesJson { get; set; } = "{}";
}

public class DebuggerStatusReport
{
    public bool IsDapAvailable { get; set; }

    public string DapPath { get; set; }

    public int AttachedSessionCount { get; set; }

    public int ActiveTracepointsCount { get; set; }

    public int CapturedSnapshotsCount { get; set; }

    public DateTime ServerTimestampUtc { get; set; } = DateTime.UtcNow;
}

public interface IDeveloperDebuggerService
{
    DebuggerStatusReport GetStatus();

    TracepointDefinition AddTracepoint(TracepointDefinition tracepoint);

    bool RemoveTracepoint(string tracepointId);

    IReadOnlyList<TracepointDefinition> GetTracepoints();

    void ClearTracepoints();

    void RecordSnapshot(TracepointSnapshot snapshot);

    IReadOnlyList<TracepointSnapshot> GetSnapshots(int limit = 50);

    void ClearSnapshots();

    TracepointSnapshot CaptureSnapshot(string tracepointId, object variables = null, string filePath = null, int lineNumber = 0);
}
