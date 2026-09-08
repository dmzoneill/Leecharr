// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Core.Torrents;

public static class TorrentPathValidator
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsReservedDeviceName(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var trimmed = segment.Trim();
        if (ReservedDeviceNames.Contains(trimmed))
        {
            return true;
        }

        var withoutExt = Path.GetFileNameWithoutExtension(trimmed);
        if (ReservedDeviceNames.Contains(withoutExt))
        {
            return true;
        }

        var firstDotIndex = trimmed.IndexOf('.');
        if (firstDotIndex > 0)
        {
            var primaryName = trimmed.Substring(0, firstDotIndex);
            if (ReservedDeviceNames.Contains(primaryName))
            {
                return true;
            }
        }

        return false;
    }

    public static string ResolveCanonicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var fullPath = Path.GetFullPath(path);

        try
        {
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    return Path.GetFullPath(target.FullName);
                }
            }
            else if (Directory.Exists(fullPath))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                var target = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    return Path.GetFullPath(target.FullName);
                }
            }
            else
            {
                var segments = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                var root = Path.GetPathRoot(fullPath);
                var current = root ?? string.Empty;

                for (var i = 0; i < segments.Length; i++)
                {
                    var next = Path.Combine(current, segments[i]);
                    if (Directory.Exists(next) || File.Exists(next))
                    {
                        var dirInfo = new DirectoryInfo(next);
                        var target = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                        if (target != null)
                        {
                            current = Path.GetFullPath(target.FullName);
                            continue;
                        }
                    }

                    current = next;
                }

                return Path.GetFullPath(current);
            }
        }
        catch
        {
            // If symlink resolution fails, fallback to GetFullPath
        }

        return fullPath;
    }

    public static bool IsStrictSubPath(string basePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        try
        {
            var fullBase = ResolveCanonicalPath(basePath);
            if (!fullBase.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !fullBase.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                fullBase += Path.DirectorySeparatorChar;
            }

            var fullTarget = ResolveCanonicalPath(targetPath);
            var targetWithSep = fullTarget;
            if (!targetWithSep.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !targetWithSep.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                targetWithSep += Path.DirectorySeparatorChar;
            }

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Target cannot be equal to base path
            if (string.Equals(fullBase, targetWithSep, comparison))
            {
                return false;
            }

            // Target must strictly start with base path including directory separator
            if (!fullTarget.StartsWith(fullBase, comparison))
            {
                return false;
            }

            var relativePart = fullTarget.Substring(fullBase.Length);
            var segments = relativePart.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (IsReservedDeviceName(segment))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.Contains('\0'))
        {
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\') ||
            (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':'))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed == "." ||
                trimmed == ".." ||
                IsReservedDeviceName(trimmed) ||
                !Organizer.FileNameSanitizer.IsValidFileNameStatic(trimmed))
            {
                return false;
            }
        }

        return true;
    }
}
