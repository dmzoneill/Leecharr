// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public interface IRssRuleRepository : IBasicRepository<RssRule>
{
    IEnumerable<RssRule> GetEnabled();
}

public class RssRuleRepository : BasicRepository<RssRule>, IRssRuleRepository
{
    public RssRuleRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<RssRule> GetEnabled()
    {
        return this.All().Where(r => r.IsEnabled);
    }
}
