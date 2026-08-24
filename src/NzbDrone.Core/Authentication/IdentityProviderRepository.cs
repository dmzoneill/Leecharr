using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class IdentityProviderRepository : BasicRepository<IdentityProviderDefinition>, IIdentityProviderRepository
{
    private readonly IDatabase _database;

    public IdentityProviderRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<IdentityProviderDefinition> GetEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<IdentityProviderDefinition>(
            $"SELECT * FROM \"{_table}\" WHERE \"IsEnabled\" = 1 ORDER BY \"Id\"");
    }

    public IdentityProviderDefinition FindByProviderId(string providerId)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<IdentityProviderDefinition>(
            $"SELECT * FROM \"{_table}\" WHERE LOWER(\"ProviderId\") = LOWER(@ProviderId)",
            new { ProviderId = providerId });
    }
}
