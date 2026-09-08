// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.DiskSpace;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DiskSpaceHealthCheck : IHealthCheck
{
    private const long ErrorThresholdBytes = 1024L * 1024 * 1024; // 1 GB
    private const long WarningThresholdBytes = 5L * 1024 * 1024 * 1024; // 5 GB
    private const double WarningPercentThreshold = 0.05; // 5%

    private readonly IDiskSpaceService diskSpaceService;

    public DiskSpaceHealthCheck(IDiskSpaceService diskSpaceService)
    {
        this.diskSpaceService = diskSpaceService;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (this.diskSpaceService == null)
        {
            return Task.FromResult(HealthCheckResult.Ok("DiskSpace"));
        }

        try
        {
            var disks = this.diskSpaceService.GetDiskSpace();
            if (disks == null || disks.Count == 0)
            {
                return Task.FromResult(HealthCheckResult.Ok("DiskSpace"));
            }

            var errors = new List<string>();
            var warnings = new List<string>();

            foreach (var disk in disks)
            {
                if (disk.TotalSpace <= 0)
                {
                    continue;
                }

                var freePercent = (double)disk.FreeSpace / disk.TotalSpace;
                var freeGb = disk.FreeSpace / (1024.0 * 1024 * 1024);

                if (disk.FreeSpace < ErrorThresholdBytes)
                {
                    errors.Add($"{disk.Path} has only {freeGb:0.00} GB free");
                }
                else if (disk.FreeSpace < WarningThresholdBytes || freePercent < WarningPercentThreshold)
                {
                    warnings.Add($"{disk.Path} has {freeGb:0.00} GB free ({freePercent:P1})");
                }
            }

            if (errors.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Error(
                    "DiskSpace",
                    $"Critically low disk space: {string.Join("; ", errors)}"));
            }

            if (warnings.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Warning(
                    "DiskSpace",
                    $"Low disk space: {string.Join("; ", warnings)}"));
            }

            return Task.FromResult(HealthCheckResult.Ok("DiskSpace"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Error(
                "DiskSpace",
                $"Disk space health check failed: {ex.Message}"));
        }
    }
}
