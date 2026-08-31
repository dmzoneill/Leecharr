// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRepository : IBasicRepository<Torrent>
{
    Torrent GetByInfoHash(string infoHash);

    bool ExistsByInfoHash(string infoHash);

    IEnumerable<Torrent> GetByCategory(string category);

    IEnumerable<Torrent> GetByStatus(TorrentStatus status);
}
