// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Common.Disk;

public readonly struct DriveSpaceSnapshot
{
    public DriveSpaceSnapshot(string mountPath, long freeBytes, long totalBytes, string driveFormat, string volumeLabel)
    {
        this.MountPath = mountPath;
        this.FreeBytes = freeBytes;
        this.TotalBytes = totalBytes;
        this.DriveFormat = driveFormat ?? string.Empty;
        this.VolumeLabel = volumeLabel ?? string.Empty;
    }

    public string MountPath { get; }

    public long FreeBytes { get; }

    public long TotalBytes { get; }

    public string DriveFormat { get; }

    public string VolumeLabel { get; }
}

public static class DriveSpaceProbe
{
    private static readonly char[] UncPathSeparators = { '\\' };

    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static bool TryReadSpace(string path, TimeSpan timeout, out DriveSpaceSnapshot snapshot)
    {
        snapshot = default;
        if (!TryResolveMount(path, out var mountPath))
        {
            return false;
        }

        if (!TryProbeDrive(mountPath, timeout, out var probe))
        {
            return false;
        }

        snapshot = probe;
        return true;
    }

    public static bool TryResolveMount(string path, out string mountPath)
    {
        mountPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var rawPath = path;
        if (rawPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            rawPath = string.Concat(@"\\", rawPath.AsSpan(8));
        }
        else if (rawPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            rawPath = rawPath.Substring(4);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
            fullPath = DiskProvider.ResolveSymlinkPath(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Logger.Trace(ex, "Failed to normalize path '{0}' for drive probe", path);
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return LinuxMountTable.TryGetLongestMountForPath(fullPath, out mountPath);
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            mountPath = GetUncMountPrefix(fullPath);
            return !string.IsNullOrWhiteSpace(mountPath);
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        mountPath = root;
        return true;
    }

    public static IEnumerable<string> GetProbeableMountPoints()
    {
        if (!OperatingSystem.IsWindows())
        {
            return LinuxMountTable.GetDiskMountPoints();
        }

        var mounts = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Network)
                {
                    continue;
                }

                mounts.Add(drive.Name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Logger.Trace(ex, "Failed to enumerate Windows drives for disk space probe");
        }

        return mounts;
    }

    public static bool TryProbeDrive(string mountPath, TimeSpan timeout, out DriveSpaceSnapshot snapshot)
    {
        snapshot = default;
        if (string.IsNullOrWhiteSpace(mountPath) || timeout <= TimeSpan.Zero)
        {
            return false;
        }

        DriveSpaceSnapshot captured = default;
        var probeTask = Task.Run(() =>
        {
            try
            {
                var drive = new DriveInfo(mountPath);
                if (!drive.IsReady)
                {
                    return;
                }

                var total = drive.TotalSize;
                if (total <= 0)
                {
                    return;
                }

                captured = new DriveSpaceSnapshot(
                    mountPath,
                    drive.AvailableFreeSpace,
                    total,
                    drive.DriveFormat,
                    drive.VolumeLabel);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Trace(ex, "Drive probe failed for mount '{0}'", mountPath);
            }
        });

        if (!probeTask.Wait(timeout))
        {
            Logger.Trace("Drive probe timed out after {0:0.#}s for mount '{1}'", timeout.TotalSeconds, mountPath);
            return false;
        }

        if (captured.TotalBytes <= 0)
        {
            return false;
        }

        snapshot = captured;
        return true;
    }

    private static string GetUncMountPrefix(string uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath) || !uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var trimmed = uncPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = trimmed.Split(UncPathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return trimmed + Path.DirectorySeparatorChar;
        }

        return $"\\{segments[0]}\\{segments[1]}{Path.DirectorySeparatorChar}";
    }
}
