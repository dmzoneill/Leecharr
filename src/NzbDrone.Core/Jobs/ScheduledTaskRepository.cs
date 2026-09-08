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
    public ScheduledTaskRepository(IDatabase database)
        : base(database)
    {
    }

    public ScheduledTask GetByTypeName(string typeName)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<ScheduledTask>(
                $"SELECT * FROM \"{this.table}\" WHERE \"TypeName\" = @TypeName",
                new { TypeName = typeName }));
    }
}
