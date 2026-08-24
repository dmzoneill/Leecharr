using System;
using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserSessionRepository : BasicRepository<UserSession>, IUserSessionRepository
{
    private readonly IDatabase _database;

    public UserSessionRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public UserSession FindBySessionToken(string token)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<UserSession>(
            $"SELECT * FROM \"{_table}\" WHERE \"SessionToken\" = @Token",
            new { Token = token });
    }

    public UserSession FindByRefreshToken(string refreshToken)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<UserSession>(
            $"SELECT * FROM \"{_table}\" WHERE \"RefreshToken\" = @RefreshToken",
            new { RefreshToken = refreshToken });
    }

    public IEnumerable<UserSession> FindByUserId(int userId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<UserSession>(
            $"SELECT * FROM \"{_table}\" WHERE \"UserId\" = @UserId ORDER BY \"LastActivity\" DESC",
            new { UserId = userId });
    }

    public void DeleteExpiredSessions()
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"Expiry\" < @Now",
            new { Now = DateTime.UtcNow });
    }

    public void DeleteByUserId(int userId)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"UserId\" = @UserId",
            new { UserId = userId });
    }
}
