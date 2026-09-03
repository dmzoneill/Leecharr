// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;

namespace NzbDrone.Core.Torrents;

public static class TorrentPathValidator
{
    public static bool IsStrictSubPath(string basePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        try
        {
            var fullBase = Path.GetFullPath(basePath);
            if (!fullBase.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !fullBase.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                fullBase += Path.DirectorySeparatorChar;
            }

            var fullTarget = Path.GetFullPath(targetPath);
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
            return fullTarget.StartsWith(fullBase, comparison);
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
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "." || trimmed == "..")
            {
                return false;
            }
        }

        return true;
    }
}
