// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jint;
using NLog;

namespace NzbDrone.Core.Developer.Debugger;

public class DeveloperDebuggerService : IDeveloperDebuggerService
{
    private const int MaxSnapshots = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    private readonly ConcurrentDictionary<string, TracepointDefinition> _tracepoints = new();
    private readonly ConcurrentDictionary<string, DateTime> _activeSessions = new();
    private readonly object _snapshotLock = new();
    private readonly LinkedList<TracepointSnapshot> _snapshots = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private string _cachedDapPath;
    private bool _dapSearched;

    public DebuggerStatusReport GetStatus()
    {
        var dapPath = GetDapExecutablePath();
        var activeCount = _tracepoints.Values.Count(t => t.IsEnabled);
        int snapshotCount;

        lock (_snapshotLock)
        {
            snapshotCount = _snapshots.Count;
        }

        return new DebuggerStatusReport
        {
            IsDapAvailable = !string.IsNullOrEmpty(dapPath),
            DapPath = dapPath,
            AttachedSessionCount = _activeSessions.Count,
            ActiveTracepointsCount = activeCount,
            CapturedSnapshotsCount = snapshotCount,
            ServerTimestampUtc = DateTime.UtcNow,
        };
    }

    public TracepointDefinition AddTracepoint(TracepointDefinition tracepoint)
    {
        if (tracepoint == null)
        {
            throw new ArgumentNullException(nameof(tracepoint));
        }

        if (string.IsNullOrWhiteSpace(tracepoint.Id))
        {
            tracepoint.Id = Guid.NewGuid().ToString("N");
        }

        if (tracepoint.CreatedAtUtc == default)
        {
            tracepoint.CreatedAtUtc = DateTime.UtcNow;
        }

        _tracepoints[tracepoint.Id] = tracepoint;
        return tracepoint;
    }

    public bool RemoveTracepoint(string tracepointId)
    {
        if (string.IsNullOrWhiteSpace(tracepointId))
        {
            return false;
        }

        return _tracepoints.TryRemove(tracepointId, out _);
    }

    public IReadOnlyList<TracepointDefinition> GetTracepoints()
    {
        return _tracepoints.Values
            .OrderBy(t => t.FilePath)
            .ThenBy(t => t.LineNumber)
            .ToList();
    }

    public void ClearTracepoints()
    {
        _tracepoints.Clear();
    }

    public void RecordSnapshot(TracepointSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(snapshot.SnapshotId))
        {
            snapshot.SnapshotId = Guid.NewGuid().ToString("N");
        }

        if (snapshot.TimestampUtc == default)
        {
            snapshot.TimestampUtc = DateTime.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.TracepointId) &&
            _tracepoints.TryGetValue(snapshot.TracepointId, out var tp))
        {
            lock (tp)
            {
                tp.HitCount++;
            }
        }

        lock (_snapshotLock)
        {
            _snapshots.AddLast(snapshot);
            while (_snapshots.Count > MaxSnapshots)
            {
                _snapshots.RemoveFirst();
            }
        }
    }

    public IReadOnlyList<TracepointSnapshot> GetSnapshots(int limit = 50)
    {
        lock (_snapshotLock)
        {
            var count = Math.Clamp(limit, 1, MaxSnapshots);
            return _snapshots.Reverse().Take(count).ToList();
        }
    }

    public void ClearSnapshots()
    {
        lock (_snapshotLock)
        {
            _snapshots.Clear();
        }
    }

    public TracepointSnapshot CaptureSnapshot(
        string tracepointId,
        object variables = null,
        string filePath = null,
        int lineNumber = 0)
    {
        TracepointDefinition tracepoint = null;
        if (!string.IsNullOrWhiteSpace(tracepointId))
        {
            _tracepoints.TryGetValue(tracepointId, out tracepoint);
        }

        if (tracepoint != null && !tracepoint.IsEnabled)
        {
            return null;
        }

        // Evaluate tracepoint condition if defined
        if (tracepoint != null && !string.IsNullOrWhiteSpace(tracepoint.Condition))
        {
            if (!EvaluateCondition(tracepoint.Condition, variables))
            {
                return null;
            }
        }

        var stackTrace = new StackTrace(1, true);
        var callerFrame = stackTrace.GetFrames()?.FirstOrDefault(f => !string.IsNullOrEmpty(f.GetFileName()));

        var resolvedFilePath = filePath
            ?? tracepoint?.FilePath
            ?? callerFrame?.GetFileName()
            ?? string.Empty;

        var resolvedLineNumber = lineNumber > 0
            ? lineNumber
            : (tracepoint?.LineNumber > 0 ? tracepoint.LineNumber : (callerFrame?.GetFileLineNumber() ?? 0));

        var variablesJson = SerializeVariables(variables);

        var snapshot = new TracepointSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            TracepointId = tracepointId ?? string.Empty,
            FilePath = resolvedFilePath,
            LineNumber = resolvedLineNumber,
            TimestampUtc = DateTime.UtcNow,
            ThreadId = Environment.CurrentManagedThreadId,
            CallStack = stackTrace.ToString(),
            VariablesJson = variablesJson,
        };

        RecordSnapshot(snapshot);
        return snapshot;
    }

    public void RegisterSession(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _activeSessions[sessionId] = DateTime.UtcNow;
        }
    }

    public void UnregisterSession(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _activeSessions.TryRemove(sessionId, out _);
        }
    }

    private string GetDapExecutablePath()
    {
        if (_dapSearched)
        {
            return _cachedDapPath;
        }

        _cachedDapPath = FindDapExecutable();
        _dapSearched = true;
        return _cachedDapPath;
    }

    private static string FindDapExecutable()
    {
        var candidates = new[] { "netcoredbg", "netcoredbg.exe", "vsdbg", "vsdbg.exe" };
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

        // Also check common user and system locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            paths.Add(Path.Combine(home, ".vsdbg"));
            paths.Add(Path.Combine(home, ".dotnet", "tools"));
            paths.Add(Path.Combine(home, ".local", "share", "vsdbg"));
        }

        paths.Add("/usr/bin");
        paths.Add("/usr/local/bin");
        paths.Add("/opt/netcoredbg");

        foreach (var dir in paths)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(dir, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return string.Empty;
    }

    private bool EvaluateCondition(string condition, object variables)
    {
        try
        {
            var engine = new Engine(options =>
            {
                options.LimitMemory(4 * 1024 * 1024);
                options.TimeoutInterval(TimeSpan.FromSeconds(1));
                options.LimitRecursion(16);
            });

            if (variables != null)
            {
                if (variables is IDictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        engine.SetValue(kvp.Key, kvp.Value);
                    }
                }
                else
                {
                    engine.SetValue("variables", variables);
                }
            }

            var eval = engine.Evaluate(condition);
            return eval.AsBoolean();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to evaluate tracepoint condition: {0}", condition);
            return true;
        }
    }

    private static string SerializeVariables(object variables)
    {
        if (variables == null)
        {
            return "{}";
        }

        if (variables is string str)
        {
            return str;
        }

        try
        {
            return JsonSerializer.Serialize(variables, JsonOptions);
        }
        catch (Exception)
        {
            return $"\"{variables.ToString().Replace("\"", "\\\"")}\"";
        }
    }
}
