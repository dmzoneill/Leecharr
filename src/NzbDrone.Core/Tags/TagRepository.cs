// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Npgsql;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags;

public interface ITagRepository : IBasicRepository<Tag>
{
    Tag GetByLabel(string label);

    Tag GetOrCreate(Tag candidate);
}

public class TagRepository : BasicRepository<Tag>, ITagRepository
{
    public TagRepository(IDatabase database)
        : base(database)
    {
    }

    public Tag GetByLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return this.All().FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase));
    }

    public Tag GetOrCreate(Tag candidate)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.Label))
        {
            return null;
        }

        var existing = this.GetByLabel(candidate.Label);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return this.Insert(candidate);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return this.GetByLabel(candidate.Label) ?? throw;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return this.GetByLabel(candidate.Label) ?? throw;
        }
    }
}
