// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Bandwidth;

public class SpeedScheduleRepository : BasicRepository<SpeedSchedule>, ISpeedScheduleRepository
{
    private readonly IDatabase database;

    public SpeedScheduleRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<SpeedSchedule> GetEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<SpeedSchedule>(
            $"SELECT * FROM \"{this.table}\" WHERE \"IsEnabled\" = @IsEnabled ORDER BY \"Priority\"",
            new { IsEnabled = true });
    }
}
