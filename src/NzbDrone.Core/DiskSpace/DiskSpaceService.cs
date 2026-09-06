// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.DiskSpace;

public interface IDiskSpaceService
{
    List<DiskSpaceInfo> GetDiskSpace();
}

public class DiskSpaceService : IDiskSpaceService
{
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IConfigService configService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DiskSpaceService(
        IAppFolderInfo appFolderInfo,
        IConfigService configService = null,
        IDiskProvider diskProvider = null,
        ICategoryService categoryService = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.configService = configService;
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.categoryService = categoryService;
    }

    public List<DiskSpaceInfo> GetDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var downloadDir = this.configService?.DownloadDir;
        if (string.IsNullOrWhiteSpace(downloadDir))
        {
            downloadDir = Directory.Exists("/downloads")
                ? "/downloads"
                : Path.Combine(this.appFolderInfo?.AppDataFolder ?? string.Empty, "downloads");
        }

        this.AddDriveInfo(result, seen, downloadDir, "Downloads");
        this.AddDriveInfo(result, seen, this.appFolderInfo?.AppDataFolder, "AppData");
        this.AddDriveInfo(result, seen, this.appFolderInfo?.StartUpFolder, "Startup");

        if (this.categoryService != null)
        {
            try
            {
                var categories = this.categoryService.GetAll();
                foreach (var cat in categories)
                {
                    if (!string.IsNullOrWhiteSpace(cat.SavePath) && Directory.Exists(cat.SavePath))
                    {
                        this.AddDriveInfo(result, seen, cat.SavePath, $"Category: {cat.Name}");
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
                        if (total > 0 && seen.Add(drive.RootDirectory.FullName))
                        {
                            result.Add(new DiskSpaceInfo
                            {
                                Path = drive.RootDirectory.FullName,
                                Label = !string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.VolumeLabel : drive.RootDirectory.FullName,
                                FreeSpace = drive.AvailableFreeSpace,
                                TotalSpace = total,
                            });
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
                if (seen.Add(path))
                {
                    result.Add(new DiskSpaceInfo
                    {
                        Path = path,
                        Label = label,
                        FreeSpace = freeSpace.Value,
                        TotalSpace = totalSpace.Value,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Could not get drive info for path {0}", path);
        }
    }
}
