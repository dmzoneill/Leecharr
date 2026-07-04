using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ArrIntegration;

public class ArrConnectionRepository : BasicRepository<ArrConnectionDefinition>, IArrConnectionRepository
{
    private readonly IDatabase _database;

    public ArrConnectionRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<ArrConnectionDefinition> GetEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<ArrConnectionDefinition>(
            $"SELECT * FROM \"{_table}\" WHERE \"Enable\" = 1 ORDER BY \"Priority\"");
    }

    public ArrConnectionDefinition GetByType(string arrType)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<ArrConnectionDefinition>(
            $"SELECT * FROM \"{_table}\" WHERE \"ArrType\" = @ArrType",
            new { ArrType = arrType });
    }
}
