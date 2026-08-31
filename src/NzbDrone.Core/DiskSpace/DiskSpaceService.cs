// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.DiskSpace;

public interface IDiskSpaceService
{
    List<DiskSpaceInfo> GetDiskSpace();
}

public class DiskSpaceService : IDiskSpaceService
{
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DiskSpaceService(IAppFolderInfo appFolderInfo)
    {
        this.appFolderInfo = appFolderInfo;
    }

    public List<DiskSpaceInfo> GetDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        this.AddDriveInfo(result, seen, this.appFolderInfo.AppDataFolder, "AppData");
        this.AddDriveInfo(result, seen, this.appFolderInfo.StartUpFolder, "Startup");

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
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || !seen.Add(root))
            {
                return;
            }

            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                result.Add(new DiskSpaceInfo
                {
                    Path = root,
                    Label = label,
                    FreeSpace = drive.AvailableFreeSpace,
                    TotalSpace = drive.TotalSize,
                });
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Could not get drive info for path {0}", path);
        }
    }
}
