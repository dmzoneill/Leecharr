// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientRepository : BasicRepository<DownloadClientDefinition>, IDownloadClientRepository
{
    public DownloadClientRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<DownloadClientDefinition> GetEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<DownloadClientDefinition>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable ORDER BY \"Priority\"",
                new { Enable = true }));
    }

    public DownloadClientDefinition GetByType(string clientType)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<DownloadClientDefinition>(
                $"SELECT * FROM \"{this.table}\" WHERE \"ClientType\" = @ClientType",
                new { ClientType = clientType }));
    }
}
