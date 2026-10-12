// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
namespace NzbDrone.Core.Packages;

public sealed class PackageExportTorrentsNotFoundException : Exception
{
    public PackageExportTorrentsNotFoundException(IReadOnlyList<int> missingTorrentIds)
        : base(BuildMessage(missingTorrentIds))
    {
        this.MissingTorrentIds = missingTorrentIds ?? Array.Empty<int>();
    }

    public IReadOnlyList<int> MissingTorrentIds { get; }

    private static string BuildMessage(IReadOnlyList<int> missingTorrentIds)
    {
        if (missingTorrentIds == null || missingTorrentIds.Count == 0)
        {
            return "None of the specified torrents were found.";
        }

        var ids = string.Join(", ", missingTorrentIds);
        return $"Torrent(s) not found for package export: {ids}.";
    }
}
