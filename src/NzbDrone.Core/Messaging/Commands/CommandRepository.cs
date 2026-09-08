// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandRepository : BasicRepository<CommandModel>, ICommandRepository
{
    public CommandRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<CommandModel> GetByStatus(CommandStatus status)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<CommandModel>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Status\" = @Status",
                new { Status = (int)status }));
    }

    public IEnumerable<CommandModel> GetRecent(int limit = 50)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<CommandModel>(
                $"SELECT * FROM \"{this.table}\" ORDER BY \"QueuedAt\" DESC LIMIT @Limit",
                new { Limit = limit }));
    }

    public void DeleteOldTerminalCommands(DateTime cutoff)
    {
        var completed = (int)CommandStatus.Completed;
        var failed = (int)CommandStatus.Failed;
        var cancelled = (int)CommandStatus.Cancelled;

        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{this.table}\" WHERE \"Status\" IN ({completed}, {failed}, {cancelled}) AND \"EndedAt\" IS NOT NULL AND \"EndedAt\" < @Cutoff",
                new { Cutoff = cutoff }));
    }
}
