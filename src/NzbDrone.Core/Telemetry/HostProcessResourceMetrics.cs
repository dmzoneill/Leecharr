// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Telemetry;

public class HostProcessResourceMetrics
{
    public double CpuProcessPercent { get; set; }

    public int CpuCores { get; set; }

    public long WorkingSetBytes { get; set; }

    public long PrivateMemoryBytes { get; set; }

    public long VirtualMemoryBytes { get; set; }

    public long ManagedHeapBytes { get; set; }

    public int GcGen0Collections { get; set; }

    public int GcGen1Collections { get; set; }

    public int GcGen2Collections { get; set; }

    public int ThreadCount { get; set; }

    public int ThreadPoolWorkerThreads { get; set; }

    public int ThreadPoolCompletionPortThreads { get; set; }

    public int HandleCount { get; set; }

    public long UptimeSeconds { get; set; }

    public List<DiskMountPointMetrics> DiskDrives { get; set; } = new();

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class DiskMountPointMetrics
{
    public string MountPoint { get; set; } = string.Empty;

    public string DriveType { get; set; } = string.Empty;

    public long TotalSpaceBytes { get; set; }

    public long FreeSpaceBytes { get; set; }

    public long UsedSpaceBytes { get; set; }

    public double UsedPercent { get; set; }
}
