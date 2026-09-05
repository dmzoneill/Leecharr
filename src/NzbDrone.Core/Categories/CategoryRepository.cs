// Copyright (c) PlaceholderCompany. All rights reserved.

using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Categories;

public class CategoryRepository : BasicRepository<Category>, ICategoryRepository
{
    private readonly IDatabase database;

    public CategoryRepository(IDatabase database, IEventAggregator eventAggregator = null)
        : base(database, eventAggregator)
    {
        this.database = database;
    }

    public Category GetByName(string name)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<Category>(
            $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Name\") = LOWER(@Name)",
            new { Name = name });
    }

    public Category GetDefault()
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<Category>(
            $"SELECT * FROM \"{this.table}\" WHERE \"IsDefault\" = @IsDefault",
            new { IsDefault = true });
    }
}
