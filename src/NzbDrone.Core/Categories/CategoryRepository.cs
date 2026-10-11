// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Categories;

public class CategoryRepository : BasicRepository<Category>, ICategoryRepository
{
    private readonly IEventAggregator eventAggregator;

    public CategoryRepository(IDatabase database, IEventAggregator eventAggregator = null)
        : base(database, eventAggregator)
    {
        this.eventAggregator = eventAggregator;
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
                $"SELECT * FROM \"{this.table}\" WHERE \"IsDefault\" = @IsDefault ORDER BY \"Id\" ASC",
                new { IsDefault = true }));
    }

    public Category InsertAsDefault(Category model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.IsDefault = true;

        var id = 0;
        this.ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();

            try
            {
                connection.Execute(
                    $"UPDATE \"{this.table}\" SET \"IsDefault\" = @False WHERE \"IsDefault\" = @True",
                    new { False = false, True = true },
                    transaction: transaction);

                if (this.database.DatabaseType == DatabaseType.SQLite)
                {
                    id = connection.ExecuteScalar<int>(
                        TableMapping.GetInsertSql(this.table, model) + "; SELECT last_insert_rowid()",
                        model,
                        transaction: transaction);
                }
                else
                {
                    id = connection.ExecuteScalar<int>(
                        TableMapping.GetInsertSql(this.table, model) + " RETURNING \"Id\"",
                        model,
                        transaction: transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        });

        model.Id = id;
        this.eventAggregator?.PublishEvent(new ModelEvent<Category>(model, ModelAction.Created));
        return model;
    }

    public Category SetAsDefault(Category model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.IsDefault = true;

        var rowsAffected = this.ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();

            try
            {
                connection.Execute(
                    $"UPDATE \"{this.table}\" SET \"IsDefault\" = @False WHERE \"IsDefault\" = @True AND \"Id\" != @Id",
                    new { False = false, True = true, Id = model.Id },
                    transaction: transaction);

                var affected = connection.Execute(
                    TableMapping.GetUpdateSql(this.table, model),
                    model,
                    transaction: transaction);

                if (affected == 0)
                {
                    throw new KeyNotFoundException($"Category with ID {model.Id} was not found.");
                }

                transaction.Commit();
                return affected;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        });

        if (rowsAffected == 0)
        {
            throw new KeyNotFoundException($"Category with ID {model.Id} was not found.");
        }

        this.eventAggregator?.PublishEvent(new ModelEvent<Category>(model, ModelAction.Updated));
        return model;
    }
}
