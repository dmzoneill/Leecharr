// Copyright (c) PlaceholderCompany. All rights reserved.

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
                "SELECT * FROM \"DownloadHistory\" WHERE \"InfoHash\" = @InfoHash ORDER BY \"Id\" DESC",
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
            sql.Append(" AND (LOWER(\"Title\") LIKE @Query OR LOWER(\"InfoHash\") LIKE @Query OR LOWER(\"PrimaryTracker\") LIKE @Query OR LOWER(\"IndexerName\") LIKE @Query OR LOWER(\"Source\") LIKE @Query OR LOWER(\"Trackers\") LIKE @Query OR LOWER(\"DataJson\") LIKE @Query)");
            parameters.Add("Query", $"%{query.Trim().ToLowerInvariant()}%");
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

    public void DeleteOlderThan(System.DateTime cutoffDate)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute("DELETE FROM \"DownloadHistory\" WHERE \"DateAdded\" < @Cutoff", new { Cutoff = cutoffDate }));
    }

    public void DeleteAll()
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute("DELETE FROM \"DownloadHistory\""));
    }

    private static void NormalizeDownloadHistory(DownloadHistory model)
    {
        if (model?.InfoHash != null)
        {
            model.InfoHash = model.InfoHash.Trim().ToLowerInvariant();
        }
    }
}
