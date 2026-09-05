// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ArrIntegration;

public class ArrConnectionRepository : BasicRepository<ArrConnectionDefinition>, IArrConnectionRepository
{
    private readonly IDatabase database;

    public ArrConnectionRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<ArrConnectionDefinition> GetEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<ArrConnectionDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable ORDER BY \"Priority\"",
            new { Enable = true });
    }

    public ArrConnectionDefinition GetByType(string arrType)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<ArrConnectionDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"ArrType\" = @ArrType",
            new { ArrType = arrType });
    }
}
