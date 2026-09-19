// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly TimeSpan queryTimeout;

    public DiskSpaceHealthCheck(
        IDiskSpaceService diskSpaceService,
        IConfigService configService = null,
        IEventAggregator eventAggregator = null)
        : this(diskSpaceService, configService, eventAggregator, TimeSpan.FromSeconds(5))
    {
    }

    public DiskSpaceHealthCheck(
        IDiskSpaceService diskSpaceService,
        IConfigService configService,
        IEventAggregator eventAggregator,
        TimeSpan queryTimeout)
    {
        this.diskSpaceService = diskSpaceService;
        this.configService = configService;
        this.eventAggregator = eventAggregator;
        this.queryTimeout = queryTimeout > TimeSpan.Zero ? queryTimeout : TimeSpan.FromSeconds(5);
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (this.diskSpaceService == null)
        {
            return HealthCheckResult.Ok("DiskSpace");
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(this.queryTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var disksTask = Task.Run(() => this.diskSpaceService.GetDiskSpace(), linkedCts.Token);
            var completedTask = await Task.WhenAny(disksTask, Task.Delay(this.queryTimeout, linkedCts.Token)).ConfigureAwait(false);

            if (completedTask != disksTask)
            {
                return HealthCheckResult.Error("DiskSpace", $"Disk space check timed out after {this.queryTimeout.TotalSeconds:0.#}s while querying disk space");
            }

            var disks = await disksTask.ConfigureAwait(false);
            if (disks == null || disks.Count == 0)
            {
                return HealthCheckResult.Ok("DiskSpace");
            }

            var errors = new List<string>();
            var warnings = new List<string>();

            var configThresholdMb = this.configService?.LowDiskSpaceThresholdMb;
            var warningThresholdBytes = configThresholdMb.HasValue && configThresholdMb.Value > 0
                ? (long)configThresholdMb.Value * 1024 * 1024
                : DefaultWarningThresholdBytes;

            var errorThresholdBytes = configThresholdMb.HasValue && configThresholdMb.Value > 0
                ? Math.Min(warningThresholdBytes / 2, ErrorThresholdBytes)
                : ErrorThresholdBytes;

            foreach (var disk in disks)
            {
                if (disk.TotalSpace <= 0)
                {
                    continue;
                }

                var freePercent = (double)disk.FreeSpace / disk.TotalSpace;
                var freeGb = disk.FreeSpace / (1024.0 * 1024 * 1024);

                if (disk.FreeSpace < errorThresholdBytes)
                {
                    errors.Add($"{disk.Path} has only {freeGb:0.00} GB free");
                    this.eventAggregator?.PublishEvent(new DiskSpaceCriticalEvent(disk.Path, disk.FreeSpace));
                    this.eventAggregator?.PublishEvent(new HealthIssueEvent(torrent: null, "DiskSpace", $"Critically low disk space on {disk.Path}: {freeGb:0.00} GB free", isResolved: false));
                }
                else if (disk.FreeSpace < warningThresholdBytes || freePercent < WarningPercentThreshold)
                {
                    warnings.Add($"{disk.Path} has {freeGb:0.00} GB free ({freePercent:P1})");
                    this.eventAggregator?.PublishEvent(new DiskSpaceLowEvent(disk.Path, disk.FreeSpace, disk.TotalSpace, freePercent));
                }
            }

            if (errors.Count > 0)
            {
                return HealthCheckResult.Error(
                    "DiskSpace",
                    $"Critically low disk space: {string.Join("; ", errors)}");
            }

            if (warnings.Count > 0)
            {
                return HealthCheckResult.Warning(
                    "DiskSpace",
                    $"Low disk space: {string.Join("; ", warnings)}");
            }

            return HealthCheckResult.Ok("DiskSpace");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Error("DiskSpace", $"Disk space check timed out after {this.queryTimeout.TotalSeconds:0.#}s while querying disk space");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Error(
                "DiskSpace",
                $"Disk space health check failed: {ex.Message}");
        }
    }
}
