// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NzbDrone.Common.Disk;

internal static class LinuxMountTable
{
    private static readonly HashSet<string> IgnoredFileSystemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "autofs",
        "bpf",
        "cgroup",
        "cgroup2",
        "configfs",
        "debugfs",
        "devpts",
        "devtmpfs",
        "fusectl",
        "hugetlbfs",
        "mqueue",
        "nsfs",
        "proc",
        "pstore",
        "securityfs",
        "sysfs",
        "tracefs",
    };

    internal static bool TryGetLongestMountForPath(string path, out string mountPoint)
    {
        mountPoint = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var mounts = ReadMountPoints();
        if (mounts.Count == 0)
        {
            return false;
        }

        var normalizedPath = NormalizePathForComparison(path);
        var bestLength = -1;
        string bestMount = null;

        foreach (var mount in mounts)
        {
            var normalizedMount = NormalizePathForComparison(mount);
            if (normalizedPath.StartsWith(normalizedMount, StringComparison.Ordinal) &&
                normalizedMount.Length > bestLength)
            {
                bestLength = normalizedMount.Length;
                bestMount = mount;
            }
        }

        if (bestMount == null)
        {
            return false;
        }

        mountPoint = bestMount;
        return true;
    }

    internal static IReadOnlyList<string> GetDiskMountPoints()
    {
        return ReadMountPoints();
    }

    private static List<string> ReadMountPoints()
    {
        var mountPoints = new List<string>();
        const string mountInfoPath = "/proc/self/mountinfo";
        if (!File.Exists(mountInfoPath))
        {
            return mountPoints;
        }

        try
        {
            foreach (var line in File.ReadLines(mountInfoPath))
            {
                if (!TryParseMountPoint(line, out var mountPoint, out var fileSystemType))
                {
                    continue;
                }

                if (IgnoredFileSystemTypes.Contains(fileSystemType))
                {
                    continue;
                }

                mountPoints.Add(mountPoint);
            }
        }
        catch (IOException)
        {
            return new List<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return new List<string>();
        }

        return mountPoints
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(m => m.Length)
            .ToList();
    }

    private static bool TryParseMountPoint(string line, out string mountPoint, out string fileSystemType)
    {
        mountPoint = null;
        fileSystemType = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return false;
        }

        var left = line.AsSpan(0, separatorIndex);
        var right = line.AsSpan(separatorIndex + 3);

        var leftParts = left.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (leftParts.Length < 5)
        {
            return false;
        }

        mountPoint = leftParts[4];
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return false;
        }

        var rightParts = right.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rightParts.Length < 1)
        {
            return false;
        }

        fileSystemType = rightParts[0];
        return true;
    }

    private static string NormalizePathForComparison(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path;
        if (!normalized.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) && normalized != "/")
        {
            normalized += Path.DirectorySeparatorChar;
        }

        return normalized;
    }
}
