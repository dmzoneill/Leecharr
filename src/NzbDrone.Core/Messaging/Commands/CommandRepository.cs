// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandRepository : BasicRepository<CommandModel>, ICommandRepository
{
    private readonly IDatabase database;

    public CommandRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<CommandModel> GetByStatus(CommandStatus status)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<CommandModel>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Status\" = @Status",
            new { Status = (int)status });
    }

    public void DeleteOldTerminalCommands(DateTime cutoff)
    {
        var completed = (int)CommandStatus.Completed;
        var failed = (int)CommandStatus.Failed;
        var cancelled = (int)CommandStatus.Cancelled;

        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"Status\" IN ({completed}, {failed}, {cancelled}) AND \"EndedAt\" IS NOT NULL AND \"EndedAt\" < @Cutoff",
            new { Cutoff = cutoff });
    }
}
