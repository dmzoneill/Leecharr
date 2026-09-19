// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentEventLogRepository : IBasicRepository<TorrentEventLog>
{
    List<TorrentEventLog> GetLogsForTorrent(int torrentId, int limit);

    void DeleteForTorrent(int torrentId);

    void DeleteAll();
}
