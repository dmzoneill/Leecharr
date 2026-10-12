// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.ArrIntegration;

public static class ArrSyncConnectionFilter
{
    public static int ResolveIntervalMinutes(ArrConnectionDefinition connection)
    {
        return connection.SyncIntervalMinutes > 0 ? connection.SyncIntervalMinutes : 15;
    }

    public static bool IsDueForScheduledSync(ArrConnectionDefinition connection, DateTime utcNow)
    {
        if (!connection.LastSyncUtc.HasValue)
        {
            return true;
        }

        return utcNow >= connection.LastSyncUtc.Value.AddMinutes(ResolveIntervalMinutes(connection));
    }

    public static IEnumerable<ArrConnectionDefinition> SelectForSync(
        IEnumerable<ArrConnectionDefinition> connections,
        DateTime utcNow,
        bool enforceRefreshInterval)
    {
        return connections.Where(connection =>
        {
            if (!connection.SyncEnabled)
            {
                return false;
            }

            return !enforceRefreshInterval || IsDueForScheduledSync(connection, utcNow);
        });
    }

    public static void RecordSuccessfulSync(IArrConnectionRepository repository, ArrConnectionDefinition connection, DateTime utcNow)
    {
        connection.LastSyncUtc = utcNow;
        repository.Update(connection);
    }
}
