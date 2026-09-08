// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
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
        return this.ExecuteWithRetry(connection =>
            connection.Query<RssRule>(
                $"SELECT * FROM \"{this.table}\" WHERE \"IsEnabled\" = @IsEnabled",
                new { IsEnabled = true }));
    }
}
