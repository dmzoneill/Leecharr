// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
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
        return this.All().Where(i => i.Enable).OrderBy(i => i.Priority);
    }

    public IEnumerable<IndexerDefinition> GetSearchEnabled()
    {
        return this.All().Where(i => i.Enable && i.EnableSearch).OrderBy(i => i.Priority);
    }

    public IEnumerable<IndexerDefinition> GetRssEnabled()
    {
        return this.All().Where(i => i.Enable && i.EnableRss).OrderBy(i => i.Priority);
    }
}
