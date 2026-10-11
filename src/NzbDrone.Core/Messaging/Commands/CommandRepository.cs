// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandRepository : BasicRepository<CommandModel>, ICommandRepository
{
    private static readonly HashSet<string> CommandEnvelopeProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "queuedAt",
        "trigger",
        "suppressMessages",
    };

    public CommandRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<CommandModel> GetByStatus(CommandStatus status)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<CommandModel>(
                "SELECT * FROM \"Commands\" WHERE \"Status\" = @Status",
                new { Status = (int)status }));
    }

    public IEnumerable<CommandModel> GetRecent(int limit = 50)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<CommandModel>(
                "SELECT * FROM \"Commands\" ORDER BY \"QueuedAt\" DESC LIMIT @Limit",
                new { Limit = limit }));
    }

    public void DeleteOldTerminalCommands(DateTime cutoff)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                "DELETE FROM \"Commands\" WHERE \"Status\" IN (@Completed, @Failed, @Cancelled) AND \"EndedAt\" IS NOT NULL AND \"EndedAt\" < @Cutoff",
                new
                {
                    Completed = (int)CommandStatus.Completed,
                    Failed = (int)CommandStatus.Failed,
                    Cancelled = (int)CommandStatus.Cancelled,
                    Cutoff = cutoff
                }));
    }

    public CommandModel FindExisting(string name, string body)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return this.ExecuteWithRetry(connection =>
        {
            return FindExistingOnConnection(connection, name, body);
        });
    }

    public CommandModel FindOrInsert(string name, string body, CommandModel candidate)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Command name is required.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(candidate);

        return this.ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();

            try
            {
                var existing = FindExistingOnConnection(connection, name, body, transaction);
                if (existing != null)
                {
                    transaction.Commit();
                    return existing;
                }

                var insertSql = TableMapping.GetInsertSql(this.table, candidate);
                int id;
                if (this.database.DatabaseType == DatabaseType.SQLite)
                {
                    id = connection.ExecuteScalar<int>(
                        insertSql + "; SELECT last_insert_rowid()",
                        candidate,
                        transaction: transaction);
                }
                else
                {
                    id = connection.ExecuteScalar<int>(
                        insertSql + " RETURNING \"Id\"",
                        candidate,
                        transaction: transaction);
                }

                candidate.Id = id;
                transaction.Commit();
                return candidate;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        });
    }

    private static CommandModel FindExistingOnConnection(
        System.Data.IDbConnection connection,
        string name,
        string body,
        System.Data.IDbTransaction transaction = null)
    {
        var altName = name.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
            ? name[..^7]
            : name + "Command";

        var candidates = connection.Query<CommandModel>(
            "SELECT * FROM \"Commands\" WHERE (\"Name\" = @Name OR \"Name\" = @AltName) AND \"Status\" IN (@Queued, @Running)",
            new
            {
                Name = name,
                AltName = altName,
                Queued = (int)CommandStatus.Queued,
                Running = (int)CommandStatus.Running
            },
            transaction: transaction);

        return candidates.FirstOrDefault(c => AreBodiesEquivalent(c.Body, body));
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
            return IsEmptyOrDefaultCommandBody(aEmpty ? b : a);
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

    private static bool IsEmptyOrDefaultCommandBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Trim() == "{}")
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!CommandEnvelopeProperties.Contains(property.Name))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
