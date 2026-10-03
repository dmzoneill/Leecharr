// Copyright (c) FeedItOut. All rights reserved.

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
        ArgumentNullException.ThrowIfNull(tracepoint);

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

    public IReadOnlyList<DebuggerSourceFileItem> GetKnownSourceFiles()
    {
        var list = new List<DebuggerSourceFileItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan physical source files if available on disk (dev / repo environment)
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var searchDirs = new[]
            {
                Path.Combine(currentDir, "src"),
                Path.Combine(currentDir, "..", "src"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src"),
            };

            foreach (var dir in searchDirs)
            {
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relPath = Path.GetRelativePath(currentDir, file).Replace('\\', '/');
                        if (!relPath.StartsWith("src/"))
                        {
                            relPath = "src/" + Path.GetRelativePath(dir, file).Replace('\\', '/');
                        }

                        if (relPath.Contains("/obj/") || relPath.Contains("/bin/") || relPath.Contains("/_output/") || relPath.Contains("/_tests/"))
                        {
                            continue;
                        }

                        if (seenPaths.Add(relPath))
                        {
                            var className = Path.GetFileNameWithoutExtension(file);
                            var lines = 0;
                            try
                            {
                                lines = File.ReadLines(file).Count();
                            }
                            catch
                            {
                                // Ignored
                            }

                            list.Add(new DebuggerSourceFileItem
                            {
                                FilePath = relPath,
                                ClassName = className,
                                Namespace = Path.GetDirectoryName(relPath)?.Replace('/', '.') ?? string.Empty,
                                Subsystem = relPath.Split('/').Skip(1).FirstOrDefault() ?? "Core",
                                LineCount = lines,
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to scan physical source files");
        }

        // 2. Reflection mapping across loaded Leecharr assemblies
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("Leecharr") == true || a.GetName().Name?.StartsWith("NzbDrone") == true);

            foreach (var assembly in assemblies)
            {
                var asmName = assembly.GetName().Name ?? string.Empty;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type.IsNested || string.IsNullOrEmpty(type.Namespace) || type.Name.StartsWith('<'))
                    {
                        continue;
                    }

                    var ns = type.Namespace;
                    var rootFolder = asmName.StartsWith("Leecharr.Api.V1") ? "src/Leecharr.Api.V1"
                        : asmName.StartsWith("Leecharr.Http") ? "src/Leecharr.Http"
                        : asmName.StartsWith("Leecharr.Common") || ns.StartsWith("NzbDrone.Common") ? "src/NzbDrone.Common"
                        : asmName.StartsWith("Leecharr.SignalR") || ns.StartsWith("NzbDrone.SignalR") ? "src/NzbDrone.SignalR"
                        : asmName.StartsWith("Leecharr.Host") || ns.StartsWith("NzbDrone.Host") ? "src/NzbDrone.Host"
                        : "src/NzbDrone.Core";

                    var subPath = ns.Replace("NzbDrone.Core.", "")
                                    .Replace("NzbDrone.Core", "")
                                    .Replace("Leecharr.Api.V1.", "")
                                    .Replace("Leecharr.Api.V1", "")
                                    .Replace("NzbDrone.Common.", "")
                                    .Replace("NzbDrone.Common", "")
                                    .Replace("Leecharr.Http.", "")
                                    .Replace("Leecharr.Http", "")
                                    .Replace("NzbDrone.Host.", "")
                                    .Replace("NzbDrone.Host", "")
                                    .Replace('.', '/');

                    var computedPath = string.IsNullOrWhiteSpace(subPath)
                        ? $"{rootFolder}/{type.Name}.cs"
                        : $"{rootFolder}/{subPath}/{type.Name}.cs";

                    if (seenPaths.Add(computedPath))
                    {
                        list.Add(new DebuggerSourceFileItem
                        {
                            FilePath = computedPath,
                            ClassName = type.Name,
                            Namespace = type.Namespace,
                            Subsystem = rootFolder.Replace("src/", ""),
                            LineCount = type.GetMethods().Length * 5 + 10,
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to reflect loaded assemblies for source files");
        }

        return list.OrderBy(f => f.FilePath).ToList().AsReadOnly();
    }

    public DebuggerSourceCodeResponse GetSourceCode(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new DebuggerSourceCodeResponse { FilePath = string.Empty, Exists = false, Content = "// No file path specified" };
        }

        var normalized = filePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..") || Path.IsPathRooted(filePath))
        {
            return new DebuggerSourceCodeResponse { FilePath = filePath, Exists = false, Content = "// Invalid or forbidden file path" };
        }

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), normalized),
            Path.Combine(Directory.GetCurrentDirectory(), "src", normalized.StartsWith("src/") ? normalized.Substring(4) : normalized),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalized),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                try
                {
                    var content = File.ReadAllText(candidate);
                    var lineCount = File.ReadLines(candidate).Count();
                    return new DebuggerSourceCodeResponse
                    {
                        FilePath = normalized,
                        Content = content,
                        LineCount = lineCount,
                        Exists = true,
                    };
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to read source file: {0}", candidate);
                }
            }
        }

        // Generate synthetic metadata preview if source file is not physically on disk
        var className = Path.GetFileNameWithoutExtension(normalized);
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .FirstOrDefault(t => string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase));

        if (type != null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"// Runtime Metadata Outline for {type.FullName}");
            sb.AppendLine($"// Physical source not mounted in container. Showing reflected type signature.\n");
            sb.AppendLine($"namespace {type.Namespace};\n");
            sb.AppendLine($"public class {type.Name}");
            sb.AppendLine("{");

            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                sb.AppendLine($"    public {prop.PropertyType.Name} {prop.Name} {{ get; set; }}");
            }

            sb.AppendLine();
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue;
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                sb.AppendLine($"    public {method.ReturnType.Name} {method.Name}({parameters});");
            }

            sb.AppendLine("}");

            var genContent = sb.ToString();
            return new DebuggerSourceCodeResponse
            {
                FilePath = normalized,
                Content = genContent,
                LineCount = genContent.Split('\n').Length,
                Exists = true,
            };
        }

        return new DebuggerSourceCodeResponse
        {
            FilePath = normalized,
            Content = $"// File '{normalized}' was not found on disk or in loaded assembly metadata.",
            LineCount = 1,
            Exists = false,
        };
    }

    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
