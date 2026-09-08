// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers;

public class TrackerEntryRepository : BasicRepository<TrackerEntry>, ITrackerEntryRepository
{
    public TrackerEntryRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<TrackerEntry> GetByTorrentId(int torrentId)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<TrackerEntry>(
                $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Tier\", \"Id\"",
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
