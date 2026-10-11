// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Trackers;

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

        var trimmed = url.Trim();
        var canonical = TrackerAnnounceUrlCanonicalizer.Canonicalize(trimmed);

        return this.ExecuteWithRetry(connection =>
        {
            var direct = connection.QueryFirstOrDefault<TrackerBoostTracker>(
                $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Url\") = LOWER(@Url)",
                new { Url = trimmed });
            if (direct != null)
            {
                return direct;
            }

            if (!string.Equals(trimmed, canonical, StringComparison.OrdinalIgnoreCase))
            {
                direct = connection.QueryFirstOrDefault<TrackerBoostTracker>(
                    $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Url\") = LOWER(@Url)",
                    new { Url = canonical });
                if (direct != null)
                {
                    return direct;
                }
            }

            if (!Uri.TryCreate(canonical, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var port = uri.Port;
            if (port <= 0)
            {
                port = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
            }

            var candidates = connection.Query<TrackerBoostTracker>(
                    $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Host\") = LOWER(@Host) AND \"Port\" = @Port",
                    new { Host = uri.Host, Port = port })
                .ToList();

            return candidates.FirstOrDefault(t => TrackerAnnounceUrlCanonicalizer.AreEquivalent(t.Url, trimmed));
        });
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
