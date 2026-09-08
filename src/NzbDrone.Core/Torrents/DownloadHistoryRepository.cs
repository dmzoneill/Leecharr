// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
{
    private readonly IDatabase database;

    public DownloadHistoryRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
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
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadHistory>(
            $"SELECT * FROM \"{this.table}\" WHERE \"InfoHash\" = @InfoHash ORDER BY \"Id\" DESC",
            new { InfoHash = normalized });
    }

    public DownloadHistory FindByTorrentId(int torrentId)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadHistory>(
            $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Id\" DESC",
            new { TorrentId = torrentId });
    }

    public List<DownloadHistory> GetHistory(string query = null, string status = null, int limit = 500)
    {
        using var connection = this.database.OpenConnection();
        var sql = new StringBuilder($"SELECT * FROM \"{this.table}\" WHERE 1=1");
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query))
        {
            sql.Append(" AND (LOWER(\"Title\") LIKE @Query OR LOWER(\"InfoHash\") LIKE @Query OR LOWER(\"PrimaryTracker\") LIKE @Query OR LOWER(\"IndexerName\") LIKE @Query)");
            parameters.Add("Query", $"%{query.Trim().ToLowerInvariant()}%");
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", System.StringComparison.OrdinalIgnoreCase))
        {
            sql.Append(" AND \"Status\" = @Status");
            parameters.Add("Status", status.Trim());
        }

        sql.Append(" ORDER BY \"DateAdded\" DESC");

        if (limit > 0)
        {
            sql.Append(" LIMIT @Limit");
            parameters.Add("Limit", limit);
        }

        return connection.Query<DownloadHistory>(sql.ToString(), parameters).ToList();
    }

    public void DeleteAll()
    {
        using var connection = this.database.OpenConnection();
        connection.Execute($"DELETE FROM \"{this.table}\"");
    }

    private static void NormalizeDownloadHistory(DownloadHistory model)
    {
        if (model?.InfoHash != null)
        {
            model.InfoHash = model.InfoHash.Trim().ToLowerInvariant();
        }
    }
}
