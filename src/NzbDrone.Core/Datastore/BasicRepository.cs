using System.Collections.Generic;
using Dapper;

namespace NzbDrone.Core.Datastore;

public interface IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    IEnumerable<TModel> All();
    TModel Get(int id);
    TModel Insert(TModel model);
    TModel Update(TModel model);
    void Delete(int id);
    void Delete(TModel model);
}

public class BasicRepository<TModel> : IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    private readonly IDatabase _database;
    protected readonly string _table;

    public BasicRepository(IDatabase database)
    {
        _database = database;
        _table = TableMapping.GetTableName(typeof(TModel));
    }

    public IEnumerable<TModel> All()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TModel>($"SELECT * FROM \"{_table}\"");
    }

    public TModel Get(int id)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<TModel>(
            $"SELECT * FROM \"{_table}\" WHERE \"Id\" = @Id",
            new { Id = id });
    }

    public TModel Insert(TModel model)
    {
        using var connection = _database.OpenConnection();

        if (_database.DatabaseType == DatabaseType.SQLite)
        {
            var id = connection.ExecuteScalar<int>(
                TableMapping.GetInsertSql(_table, model) + "; SELECT last_insert_rowid()",
                model);
            model.Id = id;
        }
        else
        {
            var id = connection.ExecuteScalar<int>(
                TableMapping.GetInsertSql(_table, model) + " RETURNING \"Id\"",
                model);
            model.Id = id;
        }

        return model;
    }

    public TModel Update(TModel model)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            TableMapping.GetUpdateSql(_table, model),
            model);
        return model;
    }

    public void Delete(int id)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"Id\" = @Id",
            new { Id = id });
    }

    public void Delete(TModel model)
    {
        Delete(model.Id);
    }
}
