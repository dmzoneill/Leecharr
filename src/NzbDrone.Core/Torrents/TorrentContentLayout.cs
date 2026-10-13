// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Torrents;

public enum TorrentContentLayout
{
    Original,
    Subfolder,
    NoSubfolder,
}

public static class TorrentContentLayoutPlanner
{
    public static IReadOnlyList<(string OldPath, string NewPath)> Plan(TorrentContentLayout layout, string torrentName, IEnumerable<string> relativePaths)
    {
        if (layout == TorrentContentLayout.Original || relativePaths == null)
        {
            return Array.Empty<(string OldPath, string NewPath)>();
        }

        var paths = relativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Normalize)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            return Array.Empty<(string OldPath, string NewPath)>();
        }

        var sharedRoot = SharedRootFolder(paths);
        if (layout == TorrentContentLayout.NoSubfolder)
        {
            if (string.IsNullOrEmpty(sharedRoot))
            {
                return Array.Empty<(string OldPath, string NewPath)>();
            }

            var stripped = new List<(string OldPath, string NewPath)>();
            foreach (var path in paths)
            {
                var remainder = StripFirstSegment(path);
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    stripped.Add((path, remainder));
                }
            }

            return stripped;
        }

        if (!string.IsNullOrEmpty(sharedRoot))
        {
            return Array.Empty<(string OldPath, string NewPath)>();
        }

        var folderName = FolderNameForTorrent(torrentName);
        if (string.IsNullOrEmpty(folderName))
        {
            return Array.Empty<(string OldPath, string NewPath)>();
        }

        return paths
            .Select(path => (path, folderName + "/" + path))
            .ToList();
    }

    public static string SharedRootFolder(IReadOnlyList<string> normalizedPaths)
    {
        if (normalizedPaths == null || normalizedPaths.Count == 0)
        {
            return string.Empty;
        }

        string root = null;
        foreach (var path in normalizedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var slash = path.IndexOf('/');
            if (slash <= 0)
            {
                return string.Empty;
            }

            var first = path.Substring(0, slash);
            if (root == null)
            {
                root = first;
            }
            else if (!string.Equals(root, first, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }

        return root ?? string.Empty;
    }

    private static string FolderNameForTorrent(string torrentName)
    {
        if (string.IsNullOrWhiteSpace(torrentName))
        {
            return string.Empty;
        }

        var normalized = torrentName.Replace('\\', '/').Trim('/');
        var slash = normalized.IndexOf('/');
        if (slash >= 0)
        {
            normalized = normalized.Substring(0, slash);
        }

        return TorrentPathValidator.SanitizePathSegment(normalized);
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string StripFirstSegment(string path)
    {
        var slash = path.IndexOf('/');
        if (slash < 0 || slash >= path.Length - 1)
        {
            return string.Empty;
        }

        return path.Substring(slash + 1);
    }
}

public static class PendingTorrentContentLayout
{
    private static readonly ConcurrentDictionary<int, TorrentContentLayout> Pending = new();

    public static void Remember(int torrentId, TorrentContentLayout layout)
    {
        if (torrentId <= 0 || layout == TorrentContentLayout.Original)
        {
            Pending.TryRemove(torrentId, out _);
            return;
        }

        Pending[torrentId] = layout;
    }

    public static bool TryTake(int torrentId, out TorrentContentLayout layout)
    {
        return Pending.TryRemove(torrentId, out layout);
    }
}
