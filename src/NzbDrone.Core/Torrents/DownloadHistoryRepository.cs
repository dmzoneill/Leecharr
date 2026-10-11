// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
{
    public DownloadHistoryRepository(IDatabase database)
        : base(database)
    {
    }

    public override DownloadHistory Insert(DownloadHistory model)
    {
        NormalizeDownloadHistory(model);
        return base.Insert(model);
    }

    public override DownloadHistory Update(DownloadHistory model)
    {
        NormalizeDownloadHistory(model);
        return base.Update(model);
    }

    public override void UpsertMany(IEnumerable<DownloadHistory> toInsert, IEnumerable<DownloadHistory> toUpdate)
    {
        if (toInsert != null)
        {
            foreach (var item in toInsert)
            {
                NormalizeDownloadHistory(item);
            }
        }

        if (toUpdate != null)
        {
            foreach (var item in toUpdate)
            {
                NormalizeDownloadHistory(item);
            }
        }

        base.UpsertMany(toInsert, toUpdate);
    }

    public DownloadHistory FindByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var normalized = infoHash.Trim().ToLowerInvariant();
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<DownloadHistory>(
                "SELECT * FROM \"DownloadHistory\" WHERE \"InfoHash\" = @InfoHash OR \"V2InfoHash\" = @InfoHash ORDER BY \"Id\" DESC",
                new { InfoHash = normalized }));
    }

    public DownloadHistory FindByTorrentId(int torrentId)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<DownloadHistory>(
                "SELECT * FROM \"DownloadHistory\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Id\" DESC",
                new { TorrentId = torrentId }));
    }

    public List<DownloadHistory> GetHistory(
        string query = null,
        string status = null,
        int limit = 500,
        int offset = 0,
        System.DateTime? startDate = null,
        System.DateTime? endDate = null)
    {
        var sql = new StringBuilder("SELECT * FROM \"DownloadHistory\" WHERE 1=1");
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var escapeClause = " ESCAPE '\\'";
            if (this.database.DatabaseType == DatabaseType.PostgreSQL)
            {
                sql.Append($" AND (\"Title\" ILIKE @Query{escapeClause} OR \"InfoHash\" ILIKE @Query{escapeClause} OR \"V2InfoHash\" ILIKE @Query{escapeClause} OR \"PrimaryTracker\" ILIKE @Query{escapeClause} OR \"IndexerName\" ILIKE @Query{escapeClause} OR \"Source\" ILIKE @Query{escapeClause} OR \"Trackers\" ILIKE @Query{escapeClause} OR \"DataJson\" ILIKE @Query{escapeClause})");
                parameters.Add("Query", SqlLikeHelper.BuildContainsPattern(query, lowercaseQuery: false));
            }
            else
            {
                sql.Append($" AND (unicode_lower(\"Title\") LIKE @Query{escapeClause} OR unicode_lower(\"InfoHash\") LIKE @Query{escapeClause} OR unicode_lower(\"V2InfoHash\") LIKE @Query{escapeClause} OR unicode_lower(\"PrimaryTracker\") LIKE @Query{escapeClause} OR unicode_lower(\"IndexerName\") LIKE @Query{escapeClause} OR unicode_lower(\"Source\") LIKE @Query{escapeClause} OR unicode_lower(\"Trackers\") LIKE @Query{escapeClause} OR unicode_lower(\"DataJson\") LIKE @Query{escapeClause})");
                parameters.Add("Query", SqlLikeHelper.BuildContainsPattern(query, lowercaseQuery: true));
            }
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", System.StringComparison.OrdinalIgnoreCase))
        {
            sql.Append(" AND \"Status\" = @Status");
            parameters.Add("Status", status.Trim());
        }

        if (startDate.HasValue)
        {
            sql.Append(" AND \"DateAdded\" >= @StartDate");
            parameters.Add("StartDate", startDate.Value);
        }

        if (endDate.HasValue)
        {
            if (endDate.Value.TimeOfDay == TimeSpan.Zero)
            {
                sql.Append(" AND \"DateAdded\" < @EndDate");
                parameters.Add("EndDate", endDate.Value.Date.AddDays(1));
            }
            else
            {
                sql.Append(" AND \"DateAdded\" <= @EndDate");
                parameters.Add("EndDate", endDate.Value);
            }
        }

        sql.Append(" ORDER BY \"DateAdded\" DESC");

        if (limit > 0)
        {
            sql.Append(" LIMIT @Limit");
            parameters.Add("Limit", limit);
        }

        if (offset > 0)
        {
            if (limit <= 0 && this.database.DatabaseType != DatabaseType.PostgreSQL)
            {
                sql.Append(" LIMIT -1");
            }

            sql.Append(" OFFSET @Offset");
            parameters.Add("Offset", offset);
        }

        return this.ExecuteWithRetry(connection =>
            connection.Query<DownloadHistory>(sql.ToString(), parameters).ToList());
    }

    public List<DownloadHistory> GetOlderThan(System.DateTime cutoffDate)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<DownloadHistory>(
                BuildRetentionSql("SELECT *"),
                new { Cutoff = cutoffDate }).ToList());
    }

    public void DeleteOlderThan(System.DateTime cutoffDate)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                BuildRetentionSql("DELETE"),
                new { Cutoff = cutoffDate }));
    }

    public void DeleteAll()
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute("DELETE FROM \"DownloadHistory\""));
    }

    private static string BuildRetentionSql(string statementPrefix)
    {
        return $"{statementPrefix} FROM \"DownloadHistory\"\n" +
               @"                  WHERE ""TorrentId"" IS NULL
                    AND COALESCE(
                      CASE WHEN ""Status"" = 'Removed' THEN ""DateRemoved"" END,
                      CASE WHEN ""Status"" = 'Completed' THEN ""DateCompleted"" END,
                      ""DateAdded"") < @Cutoff";
    }

    private static void NormalizeDownloadHistory(DownloadHistory model)
    {
        if (model?.InfoHash != null)
        {
            model.InfoHash = model.InfoHash.Trim().ToLowerInvariant();
        }

        if (model?.V2InfoHash != null)
        {
            model.V2InfoHash = model.V2InfoHash.Trim().ToLowerInvariant();
        }
    }
}
