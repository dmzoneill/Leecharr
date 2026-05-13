using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentFileRepository : BasicRepository<TorrentFile>, ITorrentFileRepository
{
    private readonly IDatabase _database;

    public TorrentFileRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<TorrentFile> GetByTorrentId(int torrentId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TorrentFile>(
            $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }

    public void DeleteByTorrentId(int torrentId)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }
}
