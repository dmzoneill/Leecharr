// Copyright (c) FeedItOut. All rights reserved.

using System;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public interface IScheduledTaskRepository : IBasicRepository<ScheduledTask>
{
    ScheduledTask GetByTypeName(string typeName);

    void UpdateDispatchStart(int id, DateTime lastStartTime, long? lastCompletedDurationSeconds);
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

    public void UpdateDispatchStart(int id, DateTime lastStartTime, long? lastCompletedDurationSeconds)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"UPDATE \"{this.table}\" SET \"LastStartTime\" = @LastStartTime, \"LastCompletedDurationSeconds\" = @LastCompletedDurationSeconds WHERE \"Id\" = @Id",
                new { Id = id, LastStartTime = lastStartTime, LastCompletedDurationSeconds = lastCompletedDurationSeconds }));
    }
}
