using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientRepository : BasicRepository<DownloadClientDefinition>, IDownloadClientRepository
{
    private readonly IDatabase _database;

    public DownloadClientRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<DownloadClientDefinition> GetEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<DownloadClientDefinition>(
            $"SELECT * FROM \"{_table}\" WHERE \"Enable\" = 1 ORDER BY \"Priority\"");
    }

    public DownloadClientDefinition GetByType(string clientType)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadClientDefinition>(
            $"SELECT * FROM \"{_table}\" WHERE \"ClientType\" = @ClientType",
            new { ClientType = clientType });
    }
}
