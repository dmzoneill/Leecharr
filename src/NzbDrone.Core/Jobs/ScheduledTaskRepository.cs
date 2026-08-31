// Copyright (c) PlaceholderCompany. All rights reserved.

using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public interface IScheduledTaskRepository : IBasicRepository<ScheduledTask>
{
    ScheduledTask GetByTypeName(string typeName);
}

public class ScheduledTaskRepository : BasicRepository<ScheduledTask>, IScheduledTaskRepository
{
    private readonly IDatabase database;

    public ScheduledTaskRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public ScheduledTask GetByTypeName(string typeName)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<ScheduledTask>(
            $"SELECT * FROM \"{this.table}\" WHERE \"TypeName\" = @TypeName",
            new { TypeName = typeName });
    }
}
