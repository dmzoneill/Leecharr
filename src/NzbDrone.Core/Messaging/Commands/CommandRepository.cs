// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    public CommandModel FindExisting(string name, string body)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var altName = name.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
            ? name[..^7]
            : name + "Command";

        return this.ExecuteWithRetry(connection =>
        {
            var candidates = connection.Query<CommandModel>(
                $"SELECT * FROM \"{this.table}\" WHERE (\"Name\" = @Name OR \"Name\" = @AltName) AND \"Status\" IN ({(int)CommandStatus.Queued}, {(int)CommandStatus.Running})",
                new { Name = name, AltName = altName });

            return candidates.FirstOrDefault(c => AreBodiesEquivalent(c.Body, body));
        });
    }

    private static bool AreBodiesEquivalent(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        var aEmpty = string.IsNullOrWhiteSpace(a) || a.Trim() == "{}";
        var bEmpty = string.IsNullOrWhiteSpace(b) || b.Trim() == "{}";
        if (aEmpty && bEmpty)
        {
            return true;
        }

        if (aEmpty != bEmpty)
        {
            return false;
        }

        try
        {
            using var docA = JsonDocument.Parse(a);
            using var docB = JsonDocument.Parse(b);
            return JsonElement.DeepEquals(docA.RootElement, docB.RootElement);
        }
        catch
        {
            return false;
        }
    }
}
