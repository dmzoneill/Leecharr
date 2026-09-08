// Copyright (c) PlaceholderCompany. All rights reserved.

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

    public void DeleteByTorrentId(int torrentId)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }
}
