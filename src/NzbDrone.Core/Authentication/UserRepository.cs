// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserRepository : BasicRepository<User>, IUserRepository
{
    private readonly IDatabase database;

    public UserRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public User FindByUsername(string username)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<User>(
            $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Username\") = LOWER(@Username)",
            new { Username = username });
    }

    public User FindByEmail(string email)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<User>(
            $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Email\") = LOWER(@Email)",
            new { Email = email });
    }

    public User FindByIdentifier(Guid identifier)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<User>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Identifier\" = @Identifier",
            new { Identifier = identifier });
    }

    public User FindByExternalId(string providerId, string externalSubjectId)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<User>(
            $"SELECT * FROM \"{this.table}\" WHERE \"ExternalProviderId\" = @ProviderId AND \"ExternalSubjectId\" = @ExternalSubjectId",
            new { ProviderId = providerId, ExternalSubjectId = externalSubjectId });
    }

    public int GetUserCount()
    {
        using var connection = this.database.OpenConnection();
        return connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{this.table}\"");
    }
}
