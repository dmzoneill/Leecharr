// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserExternalLoginRepository : BasicRepository<UserExternalLogin>, IUserExternalLoginRepository
{
    private readonly IDatabase database;

    public UserExternalLoginRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public UserExternalLogin FindByProvider(string loginProvider, string providerKey)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<UserExternalLogin>(
            $"SELECT * FROM \"{this.table}\" WHERE \"LoginProvider\" = @LoginProvider AND \"ProviderKey\" = @ProviderKey",
            new { LoginProvider = loginProvider, ProviderKey = providerKey });
    }

    public IEnumerable<UserExternalLogin> FindByUserId(int userId)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<UserExternalLogin>(
            $"SELECT * FROM \"{this.table}\" WHERE \"UserId\" = @UserId ORDER BY \"LinkedAt\" DESC",
            new { UserId = userId });
    }

    public void DeleteByUserId(int userId)
    {
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"UserId\" = @UserId",
            new { UserId = userId });
    }
}
