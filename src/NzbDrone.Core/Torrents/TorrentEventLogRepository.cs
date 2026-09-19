// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentEventLogRepository : BasicRepository<TorrentEventLog>, ITorrentEventLogRepository
{
    public TorrentEventLogRepository(IDatabase database)
        : base(database)
    {
    }

    public List<TorrentEventLog> GetLogsForTorrent(int torrentId, int limit)
    {
        if (torrentId <= 0)
        {
            return new List<TorrentEventLog>();
        }

        return this.ExecuteWithRetry(connection =>
            connection.Query<TorrentEventLog>(
                $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Timestamp\" DESC, \"Id\" DESC LIMIT @Limit",
                new { TorrentId = torrentId, Limit = limit }).ToList());
    }

    public void DeleteForTorrent(int torrentId)
    {
        if (torrentId <= 0)
        {
            return;
        }

        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }

    public void DeleteAll()
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute($"DELETE FROM \"{this.table}\""));
    }
}
