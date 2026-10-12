using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.DiskSpace;

public interface IDiskSpaceService
{
    List<DiskSpaceInfo> GetDiskSpace();

    void CheckDiskSpaceThresholds();
}

public class DiskSpaceService : IDiskSpaceService
{
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IConfigService configService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DiskSpaceService(
        IAppFolderInfo appFolderInfo,
        IConfigService configService = null,
        IDiskProvider diskProvider = null,
        ICategoryService categoryService = null,
        IEventAggregator eventAggregator = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.configService = configService;
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.categoryService = categoryService;
        this.eventAggregator = eventAggregator;
    }

    public void CheckDiskSpaceThresholds()
    {
        var disks = this.GetDiskSpace();
        foreach (var disk in disks)
        {
            this.PublishThresholdEventsIfApplicable(disk);
        }
    }

    public List<DiskSpaceInfo> GetDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenVolumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var downloadDir = this.configService?.DownloadDir;
        if (string.IsNullOrWhiteSpace(downloadDir))
        {
            var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                ? this.appFolderInfo.AppDataFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
            downloadDir = Path.Combine(appData, "downloads");
        }

        this.AddDriveInfo(result, seen, seenVolumes, downloadDir, "Downloads");

        var incompleteDir = this.configService?.IncompleteDownloadDir;
        if (!string.IsNullOrWhiteSpace(incompleteDir))
        {
            this.AddDriveInfo(result, seen, seenVolumes, incompleteDir, "Incomplete Downloads");
        }

        this.AddDriveInfo(result, seen, seenVolumes, this.appFolderInfo?.AppDataFolder, "AppData");
        this.AddDriveInfo(result, seen, seenVolumes, this.appFolderInfo?.StartUpFolder, "Startup");

        if (this.categoryService != null)
        {
            try
            {
                var categories = this.categoryService.GetAll();
                foreach (var cat in categories)
                {
                    if (!string.IsNullOrWhiteSpace(cat.SavePath) && Directory.Exists(cat.SavePath))
                    {
                        this.AddDriveInfo(result, seen, seenVolumes, cat.SavePath, $"Category: {cat.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to inspect category save paths for disk space");
            }
        }

        try
        {
            foreach (var mount in DriveSpaceProbe.GetProbeableMountPoints())
            {
                try
                {
                    if (!DriveSpaceProbe.TryProbeDrive(mount, DriveSpaceProbe.DefaultProbeTimeout, out var probe))
                    {
                        continue;
                    }

                    var volumeKey = $"{probe.DriveFormat}_{probe.TotalBytes}_{probe.VolumeLabel}_{probe.MountPath}";
                    if (seen.Add(probe.MountPath) && seenVolumes.Add(volumeKey))
                    {
                        result.Add(new DiskSpaceInfo
                        {
                            Path = probe.MountPath,
                            Label = !string.IsNullOrWhiteSpace(probe.VolumeLabel) ? probe.VolumeLabel : probe.MountPath,
                            FreeSpace = probe.FreeBytes,
                            TotalSpace = probe.TotalBytes,
                            FileSystemType = probe.DriveFormat ?? string.Empty,
                            IsReadOnly = probe.TotalBytes > 0 && probe.FreeBytes == 0,
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to read drive properties for '{0}'", mount);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to enumerate fixed drives");
        }

        return result;
    }

    private void AddDriveInfo(
        List<DiskSpaceInfo> result,
        HashSet<string> seen,
        HashSet<string> seenVolumes,
        string path,
        string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            long? freeSpace;
            long? totalSpace;
            DriveSpaceSnapshot probeSnapshot = default;
            var hasProbeSnapshot = this.diskProvider is DiskProvider concreteDiskProvider &&
                                   concreteDiskProvider.TryQueryPathSpace(path, out probeSnapshot);

            if (hasProbeSnapshot && probeSnapshot.TotalBytes > 0)
            {
                freeSpace = probeSnapshot.FreeBytes;
                totalSpace = probeSnapshot.TotalBytes;
            }
            else
            {
                freeSpace = this.diskProvider.GetAvailableSpace(path);
                totalSpace = this.diskProvider.GetTotalSize(path);
            }

            if (freeSpace.HasValue && totalSpace.HasValue && totalSpace.Value > 0)
            {
                string volumeKey;
                string driveRoot = null;
                string fileSystemType = string.Empty;
                var isReadOnly = false;
                if (hasProbeSnapshot && probeSnapshot.TotalBytes > 0)
                {
                    driveRoot = probeSnapshot.MountPath;
                    fileSystemType = probeSnapshot.DriveFormat ?? string.Empty;
                    volumeKey = $"{probeSnapshot.DriveFormat}_{probeSnapshot.TotalBytes}_{probeSnapshot.VolumeLabel}_{probeSnapshot.MountPath}";
                    isReadOnly = probeSnapshot.TotalBytes > 0 && probeSnapshot.FreeBytes == 0;
                }
                else
                {
                    var drive = this.diskProvider.GetDrive(path);
                    if (drive != null)
                    {
                        driveRoot = drive.Name;
                        fileSystemType = drive.DriveFormat ?? string.Empty;
                        volumeKey = $"{drive.DriveFormat}_{totalSpace.Value}_{drive.VolumeLabel}_{driveRoot}";
                        isReadOnly = drive.TotalSize > 0 && drive.AvailableFreeSpace == 0;
                    }
                    else
                    {
                        volumeKey = $"{totalSpace.Value}_{path}";
                    }
                }

                var isSubdirectoryOfSharedVolume = result.Any(existing =>
                    (path.StartsWith(existing.Path + "/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(existing.Path + "\\", StringComparison.OrdinalIgnoreCase)) &&
                    existing.TotalSpace == totalSpace.Value &&
                    existing.FreeSpace == freeSpace.Value);

                if (isSubdirectoryOfSharedVolume)
                {
                    return;
                }

                if (!seenVolumes.Contains(volumeKey) && !seen.Contains(path) && (string.IsNullOrWhiteSpace(driveRoot) || !seen.Contains(driveRoot)))
                {
                    seenVolumes.Add(volumeKey);
                    seen.Add(path);
                    if (!string.IsNullOrWhiteSpace(driveRoot))
                    {
                        seen.Add(driveRoot);
                    }

                    var info = new DiskSpaceInfo
                    {
                        Path = path,
                        Label = label,
                        FreeSpace = freeSpace.Value,
                        TotalSpace = totalSpace.Value,
                        FileSystemType = fileSystemType,
                        IsReadOnly = isReadOnly,
                    };
                    result.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Could not get drive info for path {0}", path);
        }
    }

    private void PublishThresholdEventsIfApplicable(DiskSpaceInfo info)
    {
        if (this.eventAggregator == null || info == null || info.TotalSpace <= 0)
        {
            return;
        }

        var thresholdMb = this.configService != null
            ? this.configService.LowDiskSpaceThresholdMb
            : 5000;

        if (thresholdMb <= 0)
        {
            return;
        }

        var warningThresholdBytes = (long)thresholdMb * 1024 * 1024;
        var criticalThresholdBytes = Math.Min(1024L * 1024 * 1024, warningThresholdBytes / 2);
        var freePercent = (double)info.FreeSpace / info.TotalSpace;

        if (info.FreeSpace < criticalThresholdBytes)
        {
            this.eventAggregator.PublishEvent(new DiskSpaceCriticalEvent(info.Path, info.FreeSpace));
        }
        else if (thresholdMb > 0 && (info.FreeSpace < warningThresholdBytes || freePercent < 0.05))
        {
            this.eventAggregator.PublishEvent(new DiskSpaceLowEvent(info.Path, info.FreeSpace, info.TotalSpace, freePercent));
        }
    }
}
