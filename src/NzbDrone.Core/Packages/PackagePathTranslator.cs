// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace NzbDrone.Core.Packages;

public static class PackagePathTranslator
{
    public static string NormalizePathSeparators(string path, char? separator = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var sep = separator ?? Path.DirectorySeparatorChar;
        var alt = sep == '/' ? '\\' : '/';
        return path.Replace(alt, sep);
    }

    public static string StripDriveLetter(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var trimmed = path.Trim();
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            var remainder = trimmed.Substring(2);
            return remainder.Length > 0 ? remainder : string.Empty;
        }

        return trimmed;
    }

    public static void ValidateZipSlip(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
        {
            throw new SecurityException($"Potential Zip-Slip attack detected in path: {path}");
        }
    }

    public static string TranslatePath(
        string path,
        string sourcePrefix = null,
        string destinationPrefix = null,
        string destinationRoot = null,
        Dictionary<string, string> remappings = null,
        char? targetSeparator = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        // 1. Strict Zip-Slip validation (disallow any .. directory traversal)
        ValidateZipSlip(path);

        var sep = targetSeparator ?? Path.DirectorySeparatorChar;

        // 2. Custom remappings dictionary
        if (remappings != null && remappings.Count > 0)
        {
            var normCurrent = NormalizeForComparison(path);
            foreach (var kvp in remappings)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    var normKvpKey = NormalizeForComparison(kvp.Key);
                    if (normCurrent.StartsWith(normKvpKey, StringComparison.OrdinalIgnoreCase))
                    {
                        var suffix = normCurrent.Substring(normKvpKey.Length).TrimStart('/');
                        var destPfx = NormalizePathSeparators(kvp.Value, sep).TrimEnd(sep);
                        return string.IsNullOrEmpty(suffix) ? destPfx : destPfx + sep + NormalizePathSeparators(suffix, sep);
                    }
                }
            }
        }

        // 3. Source Prefix -> Destination Prefix or Destination Root
        if (!string.IsNullOrWhiteSpace(sourcePrefix))
        {
            var normSource = NormalizeForComparison(sourcePrefix);
            var normCurrent = NormalizeForComparison(path);
            if (normCurrent.StartsWith(normSource, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = normCurrent.Substring(normSource.Length).TrimStart('/');
                var targetPfx = !string.IsNullOrWhiteSpace(destinationPrefix)
                    ? NormalizePathSeparators(destinationPrefix, sep).TrimEnd(sep)
                    : (!string.IsNullOrWhiteSpace(destinationRoot) ? NormalizePathSeparators(destinationRoot, sep).TrimEnd(sep) : null);

                if (targetPfx != null)
                {
                    return string.IsNullOrEmpty(suffix) ? targetPfx : targetPfx + sep + NormalizePathSeparators(suffix, sep);
                }
            }
        }

        // 4. Fallback to Destination Root
        if (!string.IsNullOrWhiteSpace(destinationRoot))
        {
            var stripped = StripDriveLetter(path);
            var relative = stripped.TrimStart('/', '\\');
            var rootPfx = NormalizePathSeparators(destinationRoot, sep).TrimEnd(sep);
            return string.IsNullOrEmpty(relative) ? rootPfx : rootPfx + sep + NormalizePathSeparators(relative, sep);
        }

        // 5. Default normalization
        return NormalizePathSeparators(path, sep);
    }

    private static string NormalizeForComparison(string p)
    {
        if (string.IsNullOrWhiteSpace(p))
        {
            return string.Empty;
        }

        var s = StripDriveLetter(p).Replace('\\', '/').TrimEnd('/');
        return s.StartsWith('/') ? s : "/" + s;
    }
}
