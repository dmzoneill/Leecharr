// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentRepository : BasicRepository<Torrent>, ITorrentRepository
{
    private readonly IDatabase database;

    public TorrentRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<Torrent>(
            $"SELECT * FROM \"{this.table}\" WHERE \"InfoHash\" = @InfoHash",
            new { InfoHash = infoHash });
    }

    public bool ExistsByInfoHash(string infoHash)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<int>(
            $"SELECT COUNT(1) FROM \"{this.table}\" WHERE \"InfoHash\" = @InfoHash",
            new { InfoHash = infoHash }) > 0;
    }

    public IEnumerable<Torrent> GetByCategory(string category)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<Torrent>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Category\" = @Category",
            new { Category = category });
    }

    public IEnumerable<Torrent> GetByStatus(TorrentStatus status)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<Torrent>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Status\" = @Status",
            new { Status = (int)status });
    }
}
