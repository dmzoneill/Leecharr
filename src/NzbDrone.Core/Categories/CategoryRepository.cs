// Copyright (c) PlaceholderCompany. All rights reserved.

using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Categories;

public class CategoryRepository : BasicRepository<Category>, ICategoryRepository
{
    public CategoryRepository(IDatabase database, IEventAggregator eventAggregator = null)
        : base(database, eventAggregator)
    {
    }

    public Category GetByName(string name)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<Category>(
                $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Name\") = LOWER(@Name)",
                new { Name = name }));
    }

    public Category GetDefault()
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<Category>(
                $"SELECT * FROM \"{this.table}\" WHERE \"IsDefault\" = @IsDefault",
                new { IsDefault = true }));
    }
}
