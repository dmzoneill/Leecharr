// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class IdentityProviderRepository : BasicRepository<IdentityProviderDefinition>, IIdentityProviderRepository
{
    private readonly IDatabase database;

    public IdentityProviderRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<IdentityProviderDefinition> GetEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<IdentityProviderDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"IsEnabled\" = @IsEnabled ORDER BY \"Id\"",
            new { IsEnabled = true });
    }

    public IdentityProviderDefinition FindByProviderId(string providerId)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<IdentityProviderDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"ProviderId\") = LOWER(@ProviderId)",
            new { ProviderId = providerId });
    }
}
