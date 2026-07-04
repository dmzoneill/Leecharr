using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Bandwidth;

public class SpeedScheduleRepository : BasicRepository<SpeedSchedule>, ISpeedScheduleRepository
{
    private readonly IDatabase _database;

    public SpeedScheduleRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<SpeedSchedule> GetEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<SpeedSchedule>(
            $"SELECT * FROM \"{_table}\" WHERE \"IsEnabled\" = 1 ORDER BY \"Priority\"");
    }
}
