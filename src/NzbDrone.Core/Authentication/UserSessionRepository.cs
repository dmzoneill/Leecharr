// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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

    public static string HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public override UserSession Insert(UserSession model)
    {
        if (model != null && !string.IsNullOrEmpty(model.SessionToken))
        {
            model.SessionToken = HashToken(model.SessionToken);
        }

        return base.Insert(model);
    }

    public UserSession FindBySessionToken(string token)
    {
        var hashedToken = HashToken(token);
        using var connection = this.database.OpenConnection();
        return connection.QueryFirstOrDefault<UserSession>(
            $"SELECT * FROM \"{this.table}\" WHERE \"SessionToken\" = @Token",
            new { Token = hashedToken });
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
        var hashedToken = HashToken(token);
        using var connection = this.database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{this.table}\" WHERE \"SessionToken\" = @Token",
            new { Token = hashedToken });
    }

    public async Task UpdateExpiryAndActivityAsync(string sessionToken, DateTime expiry, DateTime lastActivity)
    {
        var hashedToken = HashToken(sessionToken);
        using var connection = this.database.OpenConnection();
        await connection.ExecuteAsync(
            $"UPDATE \"{this.table}\" SET \"Expiry\" = @Expiry, \"LastActivity\" = @LastActivity WHERE \"SessionToken\" = @Token",
            new { Expiry = expiry, LastActivity = lastActivity, Token = hashedToken });
    }

    public async Task UpdateLastActivityAsync(string sessionToken, DateTime lastActivity)
    {
        var hashedToken = HashToken(sessionToken);
        using var connection = this.database.OpenConnection();
        await connection.ExecuteAsync(
            $"UPDATE \"{this.table}\" SET \"LastActivity\" = @LastActivity WHERE \"SessionToken\" = @Token",
            new { LastActivity = lastActivity, Token = hashedToken });
    }
}
