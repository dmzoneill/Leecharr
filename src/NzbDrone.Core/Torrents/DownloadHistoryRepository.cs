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

    public DownloadHistory FindByInfoHash(string infoHash)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadHistory>(
            $"SELECT * FROM \"{this.table}\" WHERE \"InfoHash\" = @InfoHash ORDER BY \"Id\" DESC",
            new { InfoHash = infoHash });
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
            sql.Append(" AND (\"Title\" LIKE @Query OR \"InfoHash\" LIKE @Query OR \"PrimaryTracker\" LIKE @Query OR \"IndexerName\" LIKE @Query)");
            parameters.Add("Query", $"%{query.Trim()}%");
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
}
