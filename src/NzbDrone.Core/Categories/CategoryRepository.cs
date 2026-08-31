using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Categories;

public class CategoryRepository : BasicRepository<Category>, ICategoryRepository
{
    private readonly IDatabase _database;

    public CategoryRepository(IDatabase database, IEventAggregator eventAggregator = null)
        : base(database, eventAggregator)
    {
        _database = database;
    }

    public Category GetByName(string name)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<Category>(
            $"SELECT * FROM \"{_table}\" WHERE \"Name\" = @Name",
            new { Name = name });
    }

    public Category GetDefault()
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<Category>(
            $"SELECT * FROM \"{_table}\" WHERE \"IsDefault\" = 1");
    }
}
