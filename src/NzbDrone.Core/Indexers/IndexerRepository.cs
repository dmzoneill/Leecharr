// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public interface IIndexerRepository : IBasicRepository<IndexerDefinition>
{
    IEnumerable<IndexerDefinition> GetEnabled();

    IEnumerable<IndexerDefinition> GetSearchEnabled();

    IEnumerable<IndexerDefinition> GetRssEnabled();
}

public class IndexerRepository : BasicRepository<IndexerDefinition>, IIndexerRepository
{
    private readonly IDatabase database;

    public IndexerRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<IndexerDefinition> GetEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<IndexerDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable ORDER BY \"Priority\"",
            new { Enable = true });
    }

    public IEnumerable<IndexerDefinition> GetSearchEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<IndexerDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable AND \"EnableSearch\" = @EnableSearch ORDER BY \"Priority\"",
            new { Enable = true, EnableSearch = true });
    }

    public IEnumerable<IndexerDefinition> GetRssEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<IndexerDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable AND \"EnableRss\" = @EnableRss ORDER BY \"Priority\"",
            new { Enable = true, EnableRss = true });
    }
}
