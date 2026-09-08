// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentFileRepository : IBasicRepository<TorrentFile>
{
    IEnumerable<TorrentFile> GetByTorrentId(int torrentId);

    Dictionary<int, List<TorrentFile>> GetByTorrentIds(IEnumerable<int> torrentIds);

    void DeleteByTorrentId(int torrentId);
}
