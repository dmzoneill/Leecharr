// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentFileRepository : BasicRepository<TorrentFile>, ITorrentFileRepository
{
    private readonly IDatabase database;

    public TorrentFileRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<TorrentFile> GetByTorrentId(int torrentId)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<TorrentFile>(
            $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }

    public void DeleteByTorrentId(int torrentId)
    {
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }
}
