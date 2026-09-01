// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.TrackerBoost;

public class TrackerBoostTrackerRepository : BasicRepository<TrackerBoostTracker>, ITrackerBoostTrackerRepository
{
    private readonly IDatabase database;

    public TrackerBoostTrackerRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public TrackerBoostTracker FindByUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<TrackerBoostTracker>(
            $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Url\") = LOWER(@Url)",
            new { Url = url.Trim() });
    }

    public List<TrackerBoostTracker> GetAliveTrackers()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<TrackerBoostTracker>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Enabled\" = 1 AND (\"Status\" = 1 OR \"Status\" = 2) ORDER BY \"LatencyMs\" ASC")
            .ToList();
    }

    public List<TrackerBoostTracker> GetBySource(TrackerSourceType source)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<TrackerBoostTracker>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Source\" = @Source",
            new { Source = (int)source })
            .ToList();
    }
}
