// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientRepository : BasicRepository<DownloadClientDefinition>, IDownloadClientRepository
{
    private readonly IDatabase database;

    public DownloadClientRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<DownloadClientDefinition> GetEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<DownloadClientDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable ORDER BY \"Priority\"",
            new { Enable = true });
    }

    public DownloadClientDefinition GetByType(string clientType)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadClientDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"ClientType\" = @ClientType",
            new { ClientType = clientType });
    }
}
