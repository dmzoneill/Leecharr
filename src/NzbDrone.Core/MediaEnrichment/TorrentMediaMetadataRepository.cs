// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaEnrichment;

public class TorrentMediaMetadataRepository : BasicRepository<TorrentMediaMetadata>, ITorrentMediaMetadataRepository
{
    public TorrentMediaMetadataRepository(IDatabase database)
        : base(database)
    {
    }

    public TorrentMediaMetadata GetByTorrentId(int torrentId)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<TorrentMediaMetadata>(
                $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }

    public List<TorrentMediaMetadata> GetRelated(TorrentMediaMetadata metadata)
    {
        if (metadata == null)
        {
            return new List<TorrentMediaMetadata>();
        }

        var tvdbId = NormalizeKey(metadata.TvdbId);
        var tmdbId = NormalizeKey(metadata.TmdbId);
        var imdbId = NormalizeKey(metadata.ImdbId);
        var title = NormalizeKey(metadata.Title);
        var arrType = NormalizeKey(metadata.ArrType);
        var arrMediaId = metadata.ArrMediaId;
        if (tvdbId == null && tmdbId == null && imdbId == null && arrMediaId <= 0 && (title == null || arrType == null))
        {
            return new List<TorrentMediaMetadata>();
        }

        return this.ExecuteWithRetry(connection =>
            connection.Query<TorrentMediaMetadata>(
                $@"SELECT * FROM ""{this.table}""
WHERE ""TorrentId"" <> @TorrentId
AND (
    (@TvdbId IS NOT NULL AND ""TvdbId"" = @TvdbId)
    OR (@ImdbId IS NOT NULL AND ""ImdbId"" = @ImdbId)
    OR (@ArrMediaId > 0 AND @ArrType IS NOT NULL AND ""ArrType"" = @ArrType AND ""ArrMediaId"" = @ArrMediaId)
    OR (@TmdbId IS NOT NULL AND @ArrType IS NOT NULL AND ""TmdbId"" = @TmdbId AND ""ArrType"" = @ArrType)
    OR (@Title IS NOT NULL AND @ArrType IS NOT NULL AND ""Title"" = @Title AND ""ArrType"" = @ArrType
        AND (@Year <= 0 OR ""Year"" <= 0 OR ""Year"" = @Year))
)",
                new
                {
                    metadata.TorrentId,
                    TvdbId = tvdbId,
                    TmdbId = tmdbId,
                    ImdbId = imdbId,
                    Title = title,
                    ArrType = arrType,
                    ArrMediaId = arrMediaId,
                    metadata.Year,
                }).ToList());
    }

    private static string NormalizeKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void DeleteByTorrentId(int torrentId)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }
}
