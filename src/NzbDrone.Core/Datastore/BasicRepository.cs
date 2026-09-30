using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NLog;
using Npgsql;
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
        .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
        .Or<PostgresException>(ex => ex.SqlState is "40001" or "40P01" or "55P03")
        .Or<NpgsqlException>(ex => ex.IsTransient)
        .WaitAndRetry(
            3,
            retryAttempt => TimeSpan.FromMilliseconds(50 * Math.Pow(2, retryAttempt - 1)));

    private static readonly AsyncRetryPolicy AsyncRetryPolicy = Policy
        .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
        .Or<PostgresException>(ex => ex.SqlState is "40001" or "40P01" or "55P03")
        .Or<NpgsqlException>(ex => ex.IsTransient)
        .WaitAndRetryAsync(
            3,
            retryAttempt => TimeSpan.FromMilliseconds(50 * Math.Pow(2, retryAttempt - 1)));

    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    protected readonly IDatabase database;
    private readonly IEventAggregator eventAggregator;
    protected readonly string table;
    private readonly string selectAllSql;
    private readonly string selectByIdSql;

    public BasicRepository(IDatabase database, IEventAggregator eventAggregator = null)
    {
        this.database = database;
        this.eventAggregator = eventAggregator;
        this.table = TableMapping.GetTableName(typeof(TModel));
        this.selectAllSql = GetSelectAllSql(this.table);
        this.selectByIdSql = GetSelectByIdSql(this.table);
    }

    protected TResult ExecuteWithRetry<TResult>(Func<IDbConnection, TResult> action)
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = this.database.OpenConnection();
            return action(connection);
        });
    }

    protected void ExecuteWithRetry(Action<IDbConnection> action)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = this.database.OpenConnection();
            action(connection);
        });
    }

    protected async Task<TResult> ExecuteWithRetryAsync<TResult>(Func<IDbConnection, Task<TResult>> action)
    {
        return await AsyncRetryPolicy.ExecuteAsync(async () =>
        {
            using var connection = this.database.OpenConnection();
            return await action(connection);
        });
    }

    protected async Task ExecuteWithRetryAsync(Func<IDbConnection, Task> action)
    {
        await AsyncRetryPolicy.ExecuteAsync(async () =>
        {
            using var connection = this.database.OpenConnection();
            await action(connection);
        });
    }

    public IEnumerable<TModel> All()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<TModel>(this.selectAllSql).ToList());
    }

    public TModel Get(int id)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<TModel>(
                this.selectByIdSql,
                new { Id = id }));
    }

    public virtual TModel Insert(TModel model)
    {
        var id = 0;
        this.ExecuteWithRetry(connection =>
        {
            if (this.database.DatabaseType == DatabaseType.SQLite)
            {
                id = connection.ExecuteScalar<int>(
                    TableMapping.GetInsertSql(this.table, model) + "; SELECT last_insert_rowid()",
                    model);
            }
            else
            {
                id = connection.ExecuteScalar<int>(
                    TableMapping.GetInsertSql(this.table, model) + " RETURNING \"Id\"",
                    model);
            }
        });

        model.Id = id;
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

        var insertedIds = this.ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();

            try
            {
                var localIds = new List<int>(insertList.Count);

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
                        localIds.Add(id);
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
                return localIds;
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception ex)
                {
                    Logger.Trace(ex, "Transaction rollback failed during batch insert on {0}", this.table);
                }

                throw;
            }
        });

        if (insertedIds != null)
        {
            for (var i = 0; i < insertList.Count; i++)
            {
                insertList[i].Id = insertedIds[i];
            }
        }

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
        this.ExecuteWithRetry(connection =>
        {
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
        this.ExecuteWithRetry(connection =>
        {
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

    private static string GetSelectAllSql(string tableName) => tableName switch
    {
        "ArrConnectionDefinitions" => "SELECT * FROM \"ArrConnectionDefinitions\"",
        "ArrConnections" => "SELECT * FROM \"ArrConnections\"",
        "AutomationScripts" => "SELECT * FROM \"AutomationScripts\"",
        "Categories" => "SELECT * FROM \"Categories\"",
        "Commands" => "SELECT * FROM \"Commands\"",
        "Config" => "SELECT * FROM \"Config\"",
        "DownloadClientDefinitions" => "SELECT * FROM \"DownloadClientDefinitions\"",
        "DownloadHistory" => "SELECT * FROM \"DownloadHistory\"",
        "IdentityProviders" => "SELECT * FROM \"IdentityProviders\"",
        "IndexerDefinitions" => "SELECT * FROM \"IndexerDefinitions\"",
        "NetworkSettings" => "SELECT * FROM \"NetworkSettings\"",
        "NotificationDefinitions" => "SELECT * FROM \"NotificationDefinitions\"",
        "RssRules" => "SELECT * FROM \"RssRules\"",
        "ScheduledTasks" => "SELECT * FROM \"ScheduledTasks\"",
        "SpeedSchedules" => "SELECT * FROM \"SpeedSchedules\"",
        "Tags" => "SELECT * FROM \"Tags\"",
        "TorrentEventLogs" => "SELECT * FROM \"TorrentEventLogs\"",
        "TorrentFiles" => "SELECT * FROM \"TorrentFiles\"",
        "TorrentMediaMetadata" => "SELECT * FROM \"TorrentMediaMetadata\"",
        "Torrents" => "SELECT * FROM \"Torrents\"",
        "TrackerBoostTrackers" => "SELECT * FROM \"TrackerBoostTrackers\"",
        "TrackerEntries" => "SELECT * FROM \"TrackerEntries\"",
        "TrackerMetrics" => "SELECT * FROM \"TrackerMetrics\"",
        "TrackerMetricSnapshots" => "SELECT * FROM \"TrackerMetricSnapshots\"",
        "UserExternalLogins" => "SELECT * FROM \"UserExternalLogins\"",
        "Users" => "SELECT * FROM \"Users\"",
        "UserSessions" => "SELECT * FROM \"UserSessions\"",
        _ => "SELECT * FROM \"" + SanitizeTableName(tableName) + "\""
    };

    private static string GetSelectByIdSql(string tableName) => tableName switch
    {
        "ArrConnectionDefinitions" => "SELECT * FROM \"ArrConnectionDefinitions\" WHERE \"Id\" = @Id",
        "ArrConnections" => "SELECT * FROM \"ArrConnections\" WHERE \"Id\" = @Id",
        "AutomationScripts" => "SELECT * FROM \"AutomationScripts\" WHERE \"Id\" = @Id",
        "Categories" => "SELECT * FROM \"Categories\" WHERE \"Id\" = @Id",
        "Commands" => "SELECT * FROM \"Commands\" WHERE \"Id\" = @Id",
        "Config" => "SELECT * FROM \"Config\" WHERE \"Id\" = @Id",
        "DownloadClientDefinitions" => "SELECT * FROM \"DownloadClientDefinitions\" WHERE \"Id\" = @Id",
        "DownloadHistory" => "SELECT * FROM \"DownloadHistory\" WHERE \"Id\" = @Id",
        "IdentityProviders" => "SELECT * FROM \"IdentityProviders\" WHERE \"Id\" = @Id",
        "IndexerDefinitions" => "SELECT * FROM \"IndexerDefinitions\" WHERE \"Id\" = @Id",
        "NetworkSettings" => "SELECT * FROM \"NetworkSettings\" WHERE \"Id\" = @Id",
        "NotificationDefinitions" => "SELECT * FROM \"NotificationDefinitions\" WHERE \"Id\" = @Id",
        "RssRules" => "SELECT * FROM \"RssRules\" WHERE \"Id\" = @Id",
        "ScheduledTasks" => "SELECT * FROM \"ScheduledTasks\" WHERE \"Id\" = @Id",
        "SpeedSchedules" => "SELECT * FROM \"SpeedSchedules\" WHERE \"Id\" = @Id",
        "Tags" => "SELECT * FROM \"Tags\" WHERE \"Id\" = @Id",
        "TorrentEventLogs" => "SELECT * FROM \"TorrentEventLogs\" WHERE \"Id\" = @Id",
        "TorrentFiles" => "SELECT * FROM \"TorrentFiles\" WHERE \"Id\" = @Id",
        "TorrentMediaMetadata" => "SELECT * FROM \"TorrentMediaMetadata\" WHERE \"Id\" = @Id",
        "Torrents" => "SELECT * FROM \"Torrents\" WHERE \"Id\" = @Id",
        "TrackerBoostTrackers" => "SELECT * FROM \"TrackerBoostTrackers\" WHERE \"Id\" = @Id",
        "TrackerEntries" => "SELECT * FROM \"TrackerEntries\" WHERE \"Id\" = @Id",
        "TrackerMetrics" => "SELECT * FROM \"TrackerMetrics\" WHERE \"Id\" = @Id",
        "TrackerMetricSnapshots" => "SELECT * FROM \"TrackerMetricSnapshots\" WHERE \"Id\" = @Id",
        "UserExternalLogins" => "SELECT * FROM \"UserExternalLogins\" WHERE \"Id\" = @Id",
        "Users" => "SELECT * FROM \"Users\" WHERE \"Id\" = @Id",
        "UserSessions" => "SELECT * FROM \"UserSessions\" WHERE \"Id\" = @Id",
        _ => "SELECT * FROM \"" + SanitizeTableName(tableName) + "\" WHERE \"Id\" = @Id"
    };

    private static string SanitizeTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName) || !tableName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new ArgumentException($"Invalid table name: {tableName}", nameof(tableName));
        }

        return tableName;
    }
}
