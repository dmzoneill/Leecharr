// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
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

    public Dictionary<int, List<TorrentFile>> GetByTorrentIds(IEnumerable<int> torrentIds)
    {
        var idList = torrentIds?.Distinct().ToList();
        if (idList == null || idList.Count == 0)
        {
            return new Dictionary<int, List<TorrentFile>>();
        }

        return this.ExecuteWithRetry(connection =>
        {
            var files = connection.Query<TorrentFile>(
                $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" IN @TorrentIds ORDER BY \"Id\" ASC",
                new { TorrentIds = idList });
            return files.GroupBy(f => f.TorrentId).ToDictionary(g => g.Key, g => g.ToList());
        });
    }

    public void DeleteByTorrentId(int torrentId)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }
}
