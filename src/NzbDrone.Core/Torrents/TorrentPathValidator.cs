// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Torrents;

public static class TorrentPathValidator
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static readonly char[] UniversalInvalidPathChars = new[]
    {
        ':', '*', '?', '"', '<', '>', '|',
    };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool HasUniversalInvalidChars(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (c <= 0x1F || c == 0x7F || UniversalInvalidPathChars.Contains(c))
            {
                return true;
            }
        }

        return false;
    }

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
        return ResolveCanonicalPathInternal(path, 0);
    }

    private static string ResolveCanonicalPathInternal(string path, int depth)
    {
        if (string.IsNullOrWhiteSpace(path) || depth > 40)
        {
            return path;
        }

        var fullPath = Path.GetFullPath(path);

        try
        {
            var root = Path.GetPathRoot(fullPath);
            var current = root ?? string.Empty;
            var relativePart = !string.IsNullOrEmpty(root) && fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(root.Length)
                : fullPath;
            var segments = relativePart.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < segments.Length; i++)
            {
                var next = Path.Combine(current, segments[i]);
                var resolved = ResolveSegmentLinkTarget(next);
                if (!string.Equals(resolved, next, StringComparison.Ordinal))
                {
                    current = ResolveCanonicalPathInternal(resolved, depth + 1);
                }
                else
                {
                    current = next;
                }
            }

            return Path.GetFullPath(current);
        }
        catch
        {
            // If symlink resolution fails, fallback to GetFullPath
            return fullPath;
        }
    }

    private static string ResolveSegmentLinkTarget(string currentPath)
    {
        try
        {
            if (File.Exists(currentPath))
            {
                var target = File.ResolveLinkTarget(currentPath, returnFinalTarget: true);
                if (target != null)
                {
                    return Path.GetFullPath(target.FullName);
                }
            }
            else if (Directory.Exists(currentPath))
            {
                var target = Directory.ResolveLinkTarget(currentPath, returnFinalTarget: true);
                if (target != null)
                {
                    return Path.GetFullPath(target.FullName);
                }
            }
            else
            {
                var fileInfo = new FileInfo(currentPath);
                if (fileInfo.LinkTarget != null)
                {
                    var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null)
                    {
                        return Path.GetFullPath(target.FullName);
                    }
                }

                var dirInfo = new DirectoryInfo(currentPath);
                if (dirInfo.LinkTarget != null)
                {
                    var target = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null)
                    {
                        return Path.GetFullPath(target.FullName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "Failed to resolve link target for segment '{0}'", currentPath);
        }

        return currentPath;
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
                if (IsReservedDeviceName(segment) || HasUniversalInvalidChars(segment))
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

        if (path.IndexOfAny(UniversalInvalidPathChars) >= 0)
        {
            return false;
        }

        foreach (var c in path)
        {
            if (c <= 0x1F || c == 0x7F)
            {
                return false;
            }
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
                HasUniversalInvalidChars(trimmed) ||
                !Organizer.FileNameSanitizer.IsValidFileNameStatic(trimmed))
            {
                return false;
            }
        }

        return true;
    }

    public static string SanitizeFileName(string fileName)
    {
        return Organizer.FileNameSanitizer.SanitizeFileNameStatic(fileName);
    }

    public static string SanitizePathSegment(string segment)
    {
        return Organizer.FileNameSanitizer.SanitizeFolderNameStatic(segment);
    }

    public static string SanitizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = new List<string>();

        for (var i = 0; i < segments.Length; i++)
        {
            var isLast = i == segments.Length - 1;
            var segment = segments[i];

            string sanitized;
            if (isLast && segment.Contains('.'))
            {
                sanitized = Organizer.FileNameSanitizer.SanitizeFileNameStatic(segment);
            }
            else
            {
                sanitized = Organizer.FileNameSanitizer.SanitizeFolderNameStatic(segment);
            }

            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                sanitizedSegments.Add(sanitized);
            }
        }

        return string.Join('/', sanitizedSegments);
    }
}
