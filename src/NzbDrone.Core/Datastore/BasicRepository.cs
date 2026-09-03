// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Datastore;

public interface IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    IEnumerable<TModel> All();

    TModel Get(int id);

    TModel Insert(TModel model);

    void InsertMany(IEnumerable<TModel> models)
    {
        if (models == null)
        {
            return;
        }

        foreach (var model in models)
        {
            this.Insert(model);
        }
    }

    TModel Update(TModel model);

    void Delete(int id);

    void Delete(TModel model);
}

public class BasicRepository<TModel> : IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    private readonly IDatabase database;
    private readonly IEventAggregator eventAggregator;
    protected readonly string table;

    public BasicRepository(IDatabase database, IEventAggregator eventAggregator = null)
    {
        this.database = database;
        this.eventAggregator = eventAggregator;
        this.table = TableMapping.GetTableName(typeof(TModel));
    }

    public IEnumerable<TModel> All()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<TModel>($"SELECT * FROM \"{this.table}\"");
    }

    public TModel Get(int id)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<TModel>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Id\" = @Id",
            new { Id = id });
    }

    public TModel Insert(TModel model)
    {
        using var connection = this.database.OpenConnection();

        if (this.database.DatabaseType == DatabaseType.SQLite)
        {
            var id = connection.ExecuteScalar<int>(
                TableMapping.GetInsertSql(this.table, model) + "; SELECT last_insert_rowid()",
                model);
            model.Id = id;
        }
        else
        {
            var id = connection.ExecuteScalar<int>(
                TableMapping.GetInsertSql(this.table, model) + " RETURNING \"Id\"",
                model);
            model.Id = id;
        }

        this.eventAggregator?.PublishEvent(new ModelEvent<TModel>(model, ModelAction.Created));
        return model;
    }

    public void InsertMany(IEnumerable<TModel> models)
    {
        if (models == null)
        {
            return;
        }

        var modelList = models as IList<TModel> ?? models.ToList();
        if (modelList.Count == 0)
        {
            return;
        }

        using var connection = this.database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            var isSqlite = this.database.DatabaseType == DatabaseType.SQLite;
            var insertSql = TableMapping.GetInsertSql(this.table, modelList[0]);
            var querySql = isSqlite
                ? insertSql + "; SELECT last_insert_rowid()"
                : insertSql + " RETURNING \"Id\"";

            foreach (var model in modelList)
            {
                var id = connection.ExecuteScalar<int>(querySql, model, transaction: transaction);
                model.Id = id;
            }

            transaction.Commit();

            if (this.eventAggregator != null)
            {
                foreach (var model in modelList)
                {
                    this.eventAggregator.PublishEvent(new ModelEvent<TModel>(model, ModelAction.Created));
                }
            }
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public TModel Update(TModel model)
    {
        using var connection = this.database.OpenConnection();
        connection.Execute(
            TableMapping.GetUpdateSql(this.table, model),
            model);
        this.eventAggregator?.PublishEvent(new ModelEvent<TModel>(model, ModelAction.Updated));
        return model;
    }

    public void Delete(int id)
    {
        var existing = this.Get(id);
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"Id\" = @Id",
            new { Id = id });
        this.eventAggregator?.PublishEvent(new ModelEvent<TModel>(existing ?? new TModel { Id = id }, ModelAction.Deleted));
    }

    public void Delete(TModel model)
    {
        this.Delete(model.Id);
    }
}
