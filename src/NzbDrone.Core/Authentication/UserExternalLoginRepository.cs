// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserExternalLoginRepository : BasicRepository<UserExternalLogin>, IUserExternalLoginRepository
{
    public UserExternalLoginRepository(IDatabase database)
        : base(database)
    {
    }

    public UserExternalLogin FindByProvider(string loginProvider, string providerKey)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<UserExternalLogin>(
                $"SELECT * FROM \"{this.table}\" WHERE \"LoginProvider\" = @LoginProvider AND \"ProviderKey\" = @ProviderKey",
                new { LoginProvider = loginProvider, ProviderKey = providerKey }));
    }

    public IEnumerable<UserExternalLogin> FindByUserId(int userId)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<UserExternalLogin>(
                $"SELECT * FROM \"{this.table}\" WHERE \"UserId\" = @UserId ORDER BY \"LinkedAt\" DESC",
                new { UserId = userId }));
    }

    public void DeleteByUserId(int userId)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{this.table}\" WHERE \"UserId\" = @UserId",
                new { UserId = userId }));
    }
}
