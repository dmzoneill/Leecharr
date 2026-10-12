// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;

namespace NzbDrone.Common.Disk;

public static class UnixPrivatePermissions
{
    public static readonly UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static readonly UnixFileMode PrivateFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly UnixFileMode GroupAndOtherPermissions =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static bool IsUnixLike =>
        OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() || OperatingSystem.IsMacOS();

    public static void EnsurePrivateDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("App data path must not be empty.", nameof(path));
        }

        if (File.Exists(path))
        {
            throw new IOException($"App data path '{path}' exists but is not a directory.");
        }

        if (!Directory.Exists(path))
        {
            if (IsUnixLike)
            {
                Directory.CreateDirectory(path, PrivateDirectoryMode);
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }
        else if (IsUnixLike)
        {
            File.SetUnixFileMode(path, PrivateDirectoryMode);
        }
    }

    public static void ApplyPrivateFileMode(string path)
    {
        if (!IsUnixLike || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        File.SetUnixFileMode(path, PrivateFileMode);
    }

    /// <summary>
    /// Ensures a per-process temp directory exists, is user-private (0700), and is writable by the current user.
    /// Refuses pre-existing directories that are symlinks, world/group accessible, or not owned by the current user.
    /// </summary>
    public static void EnsurePrivateTempDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Temp directory path must not be empty.", nameof(path));
        }

        if (!IsUnixLike)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return;
        }

        if (File.Exists(path) && !Directory.Exists(path))
        {
            throw new IOException($"Temp directory path '{path}' exists but is not a directory.");
        }

        if (IsPathSymlink(path))
        {
            throw new IOException($"Refusing to use symlinked temp directory '{path}'.");
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path, PrivateDirectoryMode);
            return;
        }

        var mode = File.GetUnixFileMode(path);
        if ((mode & GroupAndOtherPermissions) != UnixFileMode.None)
        {
            File.SetUnixFileMode(path, PrivateDirectoryMode);
            mode = File.GetUnixFileMode(path);
        }

        if ((mode & GroupAndOtherPermissions) != UnixFileMode.None)
        {
            throw new IOException($"Refusing to use temp directory '{path}' with group or world permissions.");
        }

        VerifyCurrentUserWritable(path);
    }

    private static bool IsPathSymlink(string path)
    {
        var directoryInfo = new DirectoryInfo(path);
        if (directoryInfo.Exists)
        {
            return directoryInfo.LinkTarget != null;
        }

        var fileInfo = new FileInfo(path);
        return fileInfo.Exists && fileInfo.LinkTarget != null;
    }

    private static void VerifyCurrentUserWritable(string directoryPath)
    {
        var probe = Path.Combine(directoryPath, ".leecharr-temp-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Refusing to use temp directory '{directoryPath}' that is not writable by the current user.",
                ex);
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch
            {
                // Best effort cleanup of the ownership probe file.
            }
        }
    }
}
