// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NLog;

namespace NzbDrone.Core.Developer.Diagnostics;

public class DeveloperDiagnosticsService : IDeveloperDiagnosticsService
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public IReadOnlyList<ThreadDiagnosticItem> GetThreads()
    {
        var items = new List<ThreadDiagnosticItem>();

        try
        {
            var currentProc = Process.GetCurrentProcess();
            var threads = currentProc.Threads;

            for (var i = 0; i < threads.Count; i++)
            {
                var t = threads[i];
                try
                {
                    var state = t.ThreadState;
                    var priority = "Unknown";
                    try
                    {
                        priority = t.PriorityLevel.ToString();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    var waitReason = "None";
                    if (state == ThreadState.Wait)
                    {
                        waitReason = "Unknown";
                        try
                        {
                            waitReason = t.WaitReason.ToString();
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }

                    items.Add(new ThreadDiagnosticItem
                    {
                        ThreadId = t.Id,
                        Name = $"Thread-{t.Id}",
                        State = state.ToString(),
                        Priority = priority,
                        IsThreadPoolThread = false,
                        IsAlive = state != ThreadState.Terminated,
                        WaitReason = waitReason,
                        CallStack = string.Empty,
                    });
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to inspect system thread {0}", t.Id);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to enumerate system threads");
        }

        return items.AsReadOnly();
    }

    public MemoryDiagnosticReport GetMemoryReport()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        return new MemoryDiagnosticReport
        {
            TotalAllocatedBytes = GC.GetTotalAllocatedBytes(),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            HeapSizeBytes = gcInfo.HeapSizeBytes,
            TotalMemoryBytes = GC.GetTotalMemory(false),
            FragmentedBytes = gcInfo.FragmentedBytes,
            PinnedObjectsCount = (int)gcInfo.PinnedObjectsCount,
        };
    }

    public EnvironmentDiagnosticReport GetEnvironmentReport()
    {
        var proc = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - proc.StartTime.ToUniversalTime();

        return new EnvironmentDiagnosticReport
        {
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            ProcessId = Environment.ProcessId,
            ProcessorCount = Environment.ProcessorCount,
            WorkingSet64 = proc.WorkingSet64,
            NonPagedSystemMemorySize64 = proc.NonpagedSystemMemorySize64,
            PagedMemorySize64 = proc.PagedMemorySize64,
            PeakWorkingSet64 = proc.PeakWorkingSet64,
            SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss"),
            ProcessUptime = uptime.ToString(@"d\.hh\:mm\:ss"),
        };
    }

    public GcCollectionResponse ForceGarbageCollection(GcCollectionRequest request)
    {
        var sw = Stopwatch.StartNew();
        var beforeMemory = GC.GetTotalMemory(false);

        var mode = request.Blocking ? GCCollectionMode.Forced : GCCollectionMode.Optimized;
        var compact = request.Compact;

        GC.Collect(Math.Clamp(request.Generation, 0, 2), mode, request.Blocking, compact);

        if (request.Blocking)
        {
            GC.WaitForPendingFinalizers();
            GC.Collect(Math.Clamp(request.Generation, 0, 2), mode, true, compact);
        }

        sw.Stop();
        var afterMemory = GC.GetTotalMemory(false);
        var freed = Math.Max(0, beforeMemory - afterMemory);

        return new GcCollectionResponse
        {
            Success = true,
            Message = $"GC Gen{request.Generation} collection finished (compact: {compact}).",
            MemoryFreedBytes = freed,
            DurationMs = sw.ElapsedMilliseconds,
        };
    }
}
