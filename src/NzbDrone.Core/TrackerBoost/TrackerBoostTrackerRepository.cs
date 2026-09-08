// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.TrackerBoost;

public class TrackerBoostTrackerRepository : BasicRepository<TrackerBoostTracker>, ITrackerBoostTrackerRepository
{
    public TrackerBoostTrackerRepository(IDatabase database)
        : base(database)
    {
    }

    public TrackerBoostTracker FindByUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<TrackerBoostTracker>(
                $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Url\") = LOWER(@Url)",
                new { Url = url.Trim() }));
    }

    public List<TrackerBoostTracker> GetAliveTrackers()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<TrackerBoostTracker>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Enabled\" = @Enabled AND (\"Status\" = 1 OR \"Status\" = 2) ORDER BY \"LatencyMs\" ASC",
                new { Enabled = true })
            .ToList());
    }

    public List<TrackerBoostTracker> GetBySource(TrackerSourceType source)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<TrackerBoostTracker>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Source\" = @Source",
                new { Source = (int)source })
            .ToList());
    }
}
