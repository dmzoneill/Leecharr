using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentRepository : BasicRepository<Torrent>, ITorrentRepository
{
    private readonly IDatabase _database;

    public TorrentRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<Torrent>(
            $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash",
            new { InfoHash = infoHash });
    }

    public bool ExistsByInfoHash(string infoHash)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<int>(
            $"SELECT COUNT(1) FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash",
            new { InfoHash = infoHash }) > 0;
    }

    public IEnumerable<Torrent> GetByCategory(string category)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<Torrent>(
            $"SELECT * FROM \"{_table}\" WHERE \"Category\" = @Category",
            new { Category = category });
    }

    public IEnumerable<Torrent> GetByStatus(TorrentStatus status)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<Torrent>(
            $"SELECT * FROM \"{_table}\" WHERE \"Status\" = @Status",
            new { Status = (int)status });
    }
}
