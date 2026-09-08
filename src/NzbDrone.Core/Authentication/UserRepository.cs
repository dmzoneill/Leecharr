// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserRepository : BasicRepository<User>, IUserRepository
{
    public UserRepository(IDatabase database)
        : base(database)
    {
    }

    public override User Insert(User model)
    {
        NormalizeUser(model);
        return base.Insert(model);
    }

    public override User Update(User model)
    {
        NormalizeUser(model);
        return base.Update(model);
    }

    public override void UpsertMany(System.Collections.Generic.IEnumerable<User> toInsert, System.Collections.Generic.IEnumerable<User> toUpdate)
    {
        if (toInsert != null)
        {
            foreach (var item in toInsert)
            {
                NormalizeUser(item);
            }
        }

        if (toUpdate != null)
        {
            foreach (var item in toUpdate)
            {
                NormalizeUser(item);
            }
        }

        base.UpsertMany(toInsert, toUpdate);
    }

    public User FindByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var normalized = username.Trim().ToLowerInvariant();
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<User>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Username\" = @Username",
                new { Username = normalized }));
    }

    public User FindByEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().ToLowerInvariant();
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<User>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Email\" = @Email",
                new { Email = normalized }));
    }

    public User FindByIdentifier(Guid identifier)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<User>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Identifier\" = @Identifier",
                new { Identifier = identifier }));
    }

    public User FindByExternalId(string providerId, string externalSubjectId)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<User>(
                $"SELECT * FROM \"{this.table}\" WHERE \"ExternalProviderId\" = @ProviderId AND \"ExternalSubjectId\" = @ExternalSubjectId",
                new { ProviderId = providerId, ExternalSubjectId = externalSubjectId }));
    }

    public int GetUserCount()
    {
        return this.ExecuteWithRetry(connection =>
            connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{this.table}\""));
    }

    private static void NormalizeUser(User model)
    {
        if (model?.Username != null)
        {
            model.Username = model.Username.Trim().ToLowerInvariant();
        }

        if (model?.Email != null)
        {
            model.Email = model.Email.Trim().ToLowerInvariant();
        }
    }
}
