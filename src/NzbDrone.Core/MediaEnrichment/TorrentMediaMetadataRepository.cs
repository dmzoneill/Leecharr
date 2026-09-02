// Copyright (c) PlaceholderCompany. All rights reserved.

using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaEnrichment;

public class TorrentMediaMetadataRepository : BasicRepository<TorrentMediaMetadata>, ITorrentMediaMetadataRepository
{
    private readonly IDatabase database;

    public TorrentMediaMetadataRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public TorrentMediaMetadata GetByTorrentId(int torrentId)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<TorrentMediaMetadata>(
            $"SELECT * FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }

    public void DeleteByTorrentId(int torrentId)
    {
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }
}
