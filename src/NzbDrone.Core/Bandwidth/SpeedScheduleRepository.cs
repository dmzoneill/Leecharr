// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Bandwidth;

public class SpeedScheduleRepository : BasicRepository<SpeedSchedule>, ISpeedScheduleRepository
{
    public SpeedScheduleRepository(IDatabase database, IEventAggregator eventAggregator = null)
        : base(database, eventAggregator)
    {
    }

    public IEnumerable<SpeedSchedule> GetEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<SpeedSchedule>(
                $"SELECT * FROM \"{this.table}\" WHERE \"IsEnabled\" = @IsEnabled ORDER BY \"Priority\"",
                new { IsEnabled = true }));
    }
}
