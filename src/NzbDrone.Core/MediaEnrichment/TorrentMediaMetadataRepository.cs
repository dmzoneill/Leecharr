using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaEnrichment;

public class TorrentMediaMetadataRepository : BasicRepository<TorrentMediaMetadata>, ITorrentMediaMetadataRepository
{
    private readonly IDatabase _database;

    public TorrentMediaMetadataRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public TorrentMediaMetadata GetByTorrentId(int torrentId)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<TorrentMediaMetadata>(
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
