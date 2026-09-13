using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DiskSpaceHealthCheck : IHealthCheck
{
    private const long ErrorThresholdBytes = 1024L * 1024 * 1024; // 1 GB
    private const long DefaultWarningThresholdBytes = 5L * 1024 * 1024 * 1024; // 5 GB
    private const double WarningPercentThreshold = 0.05; // 5%

    private readonly IDiskSpaceService diskSpaceService;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;

    public DiskSpaceHealthCheck(
        IDiskSpaceService diskSpaceService,
        IConfigService configService = null,
        IEventAggregator eventAggregator = null)
    {
        this.diskSpaceService = diskSpaceService;
        this.configService = configService;
        this.eventAggregator = eventAggregator;
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

            var configThresholdMb = this.configService?.LowDiskSpaceThresholdMb;
            var warningThresholdBytes = configThresholdMb.HasValue && configThresholdMb.Value > 0
                ? (long)configThresholdMb.Value * 1024 * 1024
                : DefaultWarningThresholdBytes;

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
                    this.eventAggregator?.PublishEvent(new DiskSpaceCriticalEvent(disk.Path, disk.FreeSpace));
                }
                else if (disk.FreeSpace < warningThresholdBytes || freePercent < WarningPercentThreshold)
                {
                    warnings.Add($"{disk.Path} has {freeGb:0.00} GB free ({freePercent:P1})");
                    this.eventAggregator?.PublishEvent(new DiskSpaceLowEvent(disk.Path, disk.FreeSpace, disk.TotalSpace, freePercent));
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
