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
    private readonly IDatabase database;

    public RssRuleRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<RssRule> GetEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<RssRule>(
            $"SELECT * FROM \"{this.table}\" WHERE \"IsEnabled\" = @IsEnabled",
            new { IsEnabled = true });
    }
}
