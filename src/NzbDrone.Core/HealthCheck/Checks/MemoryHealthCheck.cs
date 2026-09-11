// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.HealthCheck.Checks;

public class MemoryHealthCheck : IHealthCheck
{
    private const double WarningLoadThreshold = 0.90; // 90%
    private const double ErrorLoadThreshold = 0.95; // 95%
    private const long WarningFreeMemoryThresholdBytes = 256L * 1024 * 1024; // 256 MB
    private const long ErrorFreeMemoryThresholdBytes = 64L * 1024 * 1024; // 64 MB

    private readonly Func<(long TotalBytes, long MemoryLoadBytes, long WorkingSetBytes)> memoryInfoProvider;

    public MemoryHealthCheck()
        : this(null)
    {
    }

    public MemoryHealthCheck(Func<(long TotalBytes, long MemoryLoadBytes, long WorkingSetBytes)> memoryInfoProvider)
    {
        this.memoryInfoProvider = memoryInfoProvider;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            long totalBytes;
            long memoryLoadBytes;
            long workingSet;

            if (this.memoryInfoProvider != null)
            {
                (totalBytes, memoryLoadBytes, workingSet) = this.memoryInfoProvider();
            }
            else
            {
                var gcInfo = GC.GetGCMemoryInfo();
                totalBytes = gcInfo.TotalAvailableMemoryBytes;
                memoryLoadBytes = gcInfo.MemoryLoadBytes;
                workingSet = Process.GetCurrentProcess().WorkingSet64;
            }

            if (totalBytes > 0)
            {
                var freeBytes = totalBytes - memoryLoadBytes;
                var loadRatio = (double)memoryLoadBytes / totalBytes;
                var usedMb = memoryLoadBytes / (1024 * 1024);
                var totalMb = totalBytes / (1024 * 1024);
                var workingSetMb = workingSet / (1024 * 1024);

                if (this.memoryInfoProvider == null && (loadRatio >= WarningLoadThreshold || freeBytes < WarningFreeMemoryThresholdBytes))
                {
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();

                    var gcInfo = GC.GetGCMemoryInfo();
                    totalBytes = gcInfo.TotalAvailableMemoryBytes;
                    memoryLoadBytes = gcInfo.MemoryLoadBytes;
                    workingSet = Process.GetCurrentProcess().WorkingSet64;

                    if (totalBytes > 0)
                    {
                        freeBytes = totalBytes - memoryLoadBytes;
                        loadRatio = (double)memoryLoadBytes / totalBytes;
                        usedMb = memoryLoadBytes / (1024 * 1024);
                        totalMb = totalBytes / (1024 * 1024);
                        workingSetMb = workingSet / (1024 * 1024);
                    }
                }

                if (loadRatio >= ErrorLoadThreshold || freeBytes < ErrorFreeMemoryThresholdBytes)
                {
                    return Task.FromResult(HealthCheckResult.Error(
                        "Memory",
                        $"Critical memory exhaustion: {usedMb} MB / {totalMb} MB ({loadRatio:P1}) used. Process working set: {workingSetMb} MB."));
                }

                if (loadRatio >= WarningLoadThreshold || freeBytes < WarningFreeMemoryThresholdBytes)
                {
                    return Task.FromResult(HealthCheckResult.Warning(
                        "Memory",
                        $"High memory usage: {usedMb} MB / {totalMb} MB ({loadRatio:P1}) used. Process working set: {workingSetMb} MB."));
                }
            }

            return Task.FromResult(HealthCheckResult.Ok("Memory"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Error(
                "Memory",
                $"Memory health check failed: {ex.Message}"));
        }
    }
}
