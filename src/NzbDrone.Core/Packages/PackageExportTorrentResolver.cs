// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Packages;

public static class PackageExportTorrentResolver
{
    public static List<Torrent> ResolveRequiredTorrents(ITorrentService torrentService, IEnumerable<int> torrentIds)
    {
        ArgumentNullException.ThrowIfNull(torrentService);
        ArgumentNullException.ThrowIfNull(torrentIds);

        var idList = torrentIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            throw new ArgumentException("At least one torrent ID must be provided.", nameof(torrentIds));
        }

        var torrents = new List<Torrent>(idList.Count);
        var missingIds = new List<int>();

        foreach (var id in idList)
        {
            var torrent = torrentService.Get(id);
            if (torrent == null)
            {
                missingIds.Add(id);
            }
            else
            {
                torrents.Add(torrent);
            }
        }

        if (missingIds.Count > 0)
        {
            throw new PackageExportTorrentsNotFoundException(missingIds);
        }

        return torrents;
    }
}
