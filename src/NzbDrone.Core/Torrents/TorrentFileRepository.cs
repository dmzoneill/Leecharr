// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentFileRepository : BasicRepository<TorrentFile>, ITorrentFileRepository
{
    public TorrentFileRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<TorrentFile> GetByTorrentId(int torrentId)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<TorrentFile>(
                $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Id\" ASC",
                new { TorrentId = torrentId }));
    }

    public void DeleteByTorrentId(int torrentId)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }
}
