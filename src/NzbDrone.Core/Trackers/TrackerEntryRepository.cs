// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers;

public class TrackerEntryRepository : BasicRepository<TrackerEntry>, ITrackerEntryRepository
{
    private readonly IDatabase database;

    public TrackerEntryRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<TrackerEntry> GetByTorrentId(int torrentId)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<TrackerEntry>(
            $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Tier\", \"Id\"",
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
