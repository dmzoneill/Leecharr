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
}
