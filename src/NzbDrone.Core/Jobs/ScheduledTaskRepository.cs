using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public interface IScheduledTaskRepository : IBasicRepository<ScheduledTask>
{
    ScheduledTask GetByTypeName(string typeName);
}

public class ScheduledTaskRepository : BasicRepository<ScheduledTask>, IScheduledTaskRepository
{
    private readonly IDatabase _database;

    public ScheduledTaskRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public ScheduledTask GetByTypeName(string typeName)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<ScheduledTask>(
            $"SELECT * FROM \"{_table}\" WHERE \"TypeName\" = @TypeName",
            new { TypeName = typeName });
    }
}
