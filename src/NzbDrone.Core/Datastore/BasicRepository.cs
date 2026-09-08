// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using Polly;
using Polly.Retry;

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

    void UpdateMany(IEnumerable<TModel> models)
    {
        if (models == null)
        {
            return;
        }

        foreach (var model in models)
        {
            this.Update(model);
        }
    }

    void UpsertMany(IEnumerable<TModel> toInsert, IEnumerable<TModel> toUpdate)
    {
        if (toInsert != null)
        {
            this.InsertMany(toInsert);
        }

        if (toUpdate != null)
        {
            this.UpdateMany(toUpdate);
        }
    }

    void Delete(int id);

    void Delete(TModel model);
}

public class BasicRepository<TModel> : IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    private static readonly RetryPolicy RetryPolicy = Policy
        .Handle<SqliteException>(ex => ex.SqliteErrorCode == 5)
        .WaitAndRetry(
            3,
            retryAttempt => TimeSpan.FromMilliseconds(50 * Math.Pow(2, retryAttempt - 1)));

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
        return RetryPolicy.Execute(() =>
        {
            using var connection = this.database.OpenConnection();
            return connection.Query<TModel>($"SELECT * FROM \"{this.table}\"").ToList();
        });
    }

    public TModel Get(int id)
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = this.database.OpenConnection();
            return connection.QueryFirstOrDefault<TModel>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Id\" = @Id",
                new { Id = id });
        });
    }

    public virtual TModel Insert(TModel model)
    {
        RetryPolicy.Execute(() =>
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
        });

        this.eventAggregator?.PublishEvent(new ModelEvent<TModel>(model, ModelAction.Created));
        return model;
    }

    public virtual void InsertMany(IEnumerable<TModel> models)
    {
        this.UpsertMany(models, null);
    }

    public virtual void UpdateMany(IEnumerable<TModel> models)
    {
        this.UpsertMany(null, models);
    }

    public virtual void UpsertMany(IEnumerable<TModel> toInsert, IEnumerable<TModel> toUpdate)
    {
        var insertList = toInsert as IList<TModel> ?? toInsert?.ToList() ?? new List<TModel>();
        var updateList = toUpdate as IList<TModel> ?? toUpdate?.ToList() ?? new List<TModel>();

        if (insertList.Count == 0 && updateList.Count == 0)
        {
            return;
        }

        RetryPolicy.Execute(() =>
        {
            using var connection = this.database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (insertList.Count > 0)
                {
                    var isSqlite = this.database.DatabaseType == DatabaseType.SQLite;
                    var insertSql = TableMapping.GetInsertSql(this.table, insertList[0]);
                    var querySql = isSqlite
                        ? insertSql + "; SELECT last_insert_rowid()"
                        : insertSql + " RETURNING \"Id\"";

                    foreach (var model in insertList)
                    {
                        var id = connection.ExecuteScalar<int>(querySql, model, transaction: transaction);
                        model.Id = id;
                    }
                }

                if (updateList.Count > 0)
                {
                    var updateSql = TableMapping.GetUpdateSql(this.table, updateList[0]);

                    foreach (var model in updateList)
                    {
                        connection.Execute(updateSql, model, transaction: transaction);
                    }
                }

                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Ignore rollback exceptions if transaction is already completed
                }

                throw;
            }
        });

        if (this.eventAggregator != null)
        {
            foreach (var model in insertList)
            {
                this.eventAggregator.PublishEvent(new ModelEvent<TModel>(model, ModelAction.Created));
            }

            foreach (var model in updateList)
            {
                this.eventAggregator.PublishEvent(new ModelEvent<TModel>(model, ModelAction.Updated));
            }
        }
    }

    public virtual TModel Update(TModel model)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = this.database.OpenConnection();
            connection.Execute(
                TableMapping.GetUpdateSql(this.table, model),
                model);
        });

        this.eventAggregator?.PublishEvent(new ModelEvent<TModel>(model, ModelAction.Updated));
        return model;
    }

    public virtual void Delete(int id)
    {
        var existing = this.Get(id);
        RetryPolicy.Execute(() =>
        {
            using var connection = this.database.OpenConnection();
            connection.Execute(
                TableMapping.GetDeleteSql<TModel>(this.table),
                new { Id = id });
        });

        this.eventAggregator?.PublishEvent(new ModelEvent<TModel>(existing ?? new TModel { Id = id }, ModelAction.Deleted));
    }

    public void Delete(TModel model)
    {
        this.Delete(model.Id);
    }
}
