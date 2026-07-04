using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers;

public class TrackerEntryRepository : BasicRepository<TrackerEntry>, ITrackerEntryRepository
{
    private readonly IDatabase _database;

    public TrackerEntryRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<TrackerEntry> GetByTorrentId(int torrentId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TrackerEntry>(
            $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Tier\", \"Id\"",
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
