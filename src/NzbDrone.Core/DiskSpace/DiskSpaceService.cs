using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.DiskSpace;

public interface IDiskSpaceService
{
    List<DiskSpaceInfo> GetDiskSpace();
}

public class DiskSpaceService : IDiskSpaceService
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public DiskSpaceService(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
    }

    public List<DiskSpaceInfo> GetDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDriveInfo(result, seen, _appFolderInfo.AppDataFolder, "AppData");
        AddDriveInfo(result, seen, _appFolderInfo.StartUpFolder, "Startup");

        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network))
                .ToList();

            foreach (var drive in drives)
            {
                if (seen.Add(drive.RootDirectory.FullName))
                {
                    result.Add(new DiskSpaceInfo
                    {
                        Path = drive.RootDirectory.FullName,
                        Label = !string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.VolumeLabel : drive.RootDirectory.FullName,
                        FreeSpace = drive.AvailableFreeSpace,
                        TotalSpace = drive.TotalSize,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enumerate fixed drives");
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
            if (drive.IsReady)
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
            _logger.Debug(ex, "Could not get drive info for path {0}", path);
        }
    }
}
