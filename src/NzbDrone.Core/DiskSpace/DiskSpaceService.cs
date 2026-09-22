using System;
using System.Collections.Generic;
using System.IO;
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
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                try
                {
                    if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Network))
                    {
                        var total = drive.TotalSize;
                        var volumeKey = $"{drive.DriveFormat}_{total}_{drive.VolumeLabel}_{drive.RootDirectory.FullName}";
                        if (total > 0 && seen.Add(drive.RootDirectory.FullName) && seenVolumes.Add(volumeKey))
                        {
                            var info = new DiskSpaceInfo
                            {
                                Path = drive.RootDirectory.FullName,
                                Label = !string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.VolumeLabel : drive.RootDirectory.FullName,
                                FreeSpace = drive.AvailableFreeSpace,
                                TotalSpace = total,
                                FileSystemType = drive.DriveFormat ?? string.Empty,
                                IsReadOnly = total > 0 && drive.AvailableFreeSpace == 0,
                            };
                            result.Add(info);
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible virtual filesystem mounts
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

            var freeSpace = this.diskProvider.GetAvailableSpace(path);
            var totalSpace = this.diskProvider.GetTotalSize(path);

            if (freeSpace.HasValue && totalSpace.HasValue && totalSpace.Value > 0)
            {
                var drive = this.diskProvider.GetDrive(path);
                string volumeKey;
                string driveRoot = null;
                if (drive != null)
                {
                    driveRoot = drive.RootDirectory.FullName;
                    volumeKey = $"{drive.DriveFormat}_{totalSpace.Value}_{drive.VolumeLabel}_{driveRoot}";
                }
                else
                {
                    var root = Path.GetPathRoot(path);
                    driveRoot = root;
                    volumeKey = !string.IsNullOrEmpty(root) ? $"{totalSpace.Value}_{root}" : $"{totalSpace.Value}_{path}";
                }

                if (!seenVolumes.Contains(volumeKey) && !seen.Contains(path))
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
                        FileSystemType = drive?.DriveFormat ?? string.Empty,
                        IsReadOnly = drive != null && drive.TotalSize > 0 && drive.AvailableFreeSpace == 0,
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

        var thresholdMb = this.configService?.LowDiskSpaceThresholdMb > 0
            ? this.configService.LowDiskSpaceThresholdMb
            : 5000;
        var warningThresholdBytes = (long)thresholdMb * 1024 * 1024;
        var criticalThresholdBytes = Math.Min(1024L * 1024 * 1024, warningThresholdBytes / 2);
        var freePercent = (double)info.FreeSpace / info.TotalSpace;

        if (info.FreeSpace < criticalThresholdBytes)
        {
            this.eventAggregator.PublishEvent(new DiskSpaceCriticalEvent(info.Path, info.FreeSpace));
        }
        else if (info.FreeSpace < warningThresholdBytes || freePercent < 0.05)
        {
            this.eventAggregator.PublishEvent(new DiskSpaceLowEvent(info.Path, info.FreeSpace, info.TotalSpace, freePercent));
        }
    }
}
