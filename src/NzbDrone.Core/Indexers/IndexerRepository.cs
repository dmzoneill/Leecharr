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
    public IndexerRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<IndexerDefinition> GetEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<IndexerDefinition>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable ORDER BY \"Priority\"",
                new { Enable = true }));
    }

    public IEnumerable<IndexerDefinition> GetSearchEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<IndexerDefinition>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable AND \"EnableSearch\" = @EnableSearch ORDER BY \"Priority\"",
                new { Enable = true, EnableSearch = true }));
    }

    public IEnumerable<IndexerDefinition> GetRssEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<IndexerDefinition>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable AND \"EnableRss\" = @EnableRss ORDER BY \"Priority\"",
                new { Enable = true, EnableRss = true }));
    }
}
