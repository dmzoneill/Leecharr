using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Categories;

public class CategoryRepository : BasicRepository<Category>, ICategoryRepository
{
    private readonly IDatabase _database;

    public CategoryRepository(IDatabase database)
        : base(database)
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
