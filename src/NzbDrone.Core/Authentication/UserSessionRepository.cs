// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserSessionRepository : BasicRepository<UserSession>, IUserSessionRepository
{
    private readonly IDatabase database;

    public UserSessionRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public UserSession FindBySessionToken(string token)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<UserSession>(
            $"SELECT * FROM \"{this.table}\" WHERE \"SessionToken\" = @Token",
            new { Token = token });
    }

    public UserSession FindByRefreshToken(string refreshToken)
    {
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<UserSession>(
            $"SELECT * FROM \"{this.table}\" WHERE \"RefreshToken\" = @RefreshToken",
            new { RefreshToken = refreshToken });
    }

    public IEnumerable<UserSession> FindByUserId(int userId)
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<UserSession>(
            $"SELECT * FROM \"{this.table}\" WHERE \"UserId\" = @UserId ORDER BY \"LastActivity\" DESC",
            new { UserId = userId });
    }

    public void DeleteExpiredSessions()
    {
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"Expiry\" < @Now",
            new { Now = DateTime.UtcNow });
    }

    public async Task<int> PruneExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = this.database.OpenConnection();
        return await connection.ExecuteAsync(
            $"DELETE FROM \"{this.table}\" WHERE \"Expiry\" < @Now",
            new { Now = DateTime.UtcNow });
    }

    public void DeleteByUserId(int userId)
    {
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"UserId\" = @UserId",
            new { UserId = userId });
    }

    public void RevokeSession(string token)
    {
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"SessionToken\" = @Token",
            new { Token = token });
    }

    public async Task UpdateExpiryAndActivityAsync(string sessionToken, DateTime expiry, DateTime lastActivity)
    {
        using var connection = this.database.OpenConnection();
        await connection.ExecuteAsync(
            $"UPDATE \"{this.table}\" SET \"Expiry\" = @Expiry, \"LastActivity\" = @LastActivity WHERE \"SessionToken\" = @Token",
            new { Expiry = expiry, LastActivity = lastActivity, Token = sessionToken });
    }

    public async Task UpdateLastActivityAsync(string sessionToken, DateTime lastActivity)
    {
        using var connection = this.database.OpenConnection();
        await connection.ExecuteAsync(
            $"UPDATE \"{this.table}\" SET \"LastActivity\" = @LastActivity WHERE \"SessionToken\" = @Token",
            new { LastActivity = lastActivity, Token = sessionToken });
    }
}
