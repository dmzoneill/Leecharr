// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Notifications;

public interface INotificationRepository : IBasicRepository<NotificationDefinition>
{
    IEnumerable<NotificationDefinition> GetEnabled();
}

public class NotificationRepository : BasicRepository<NotificationDefinition>, INotificationRepository
{
    private readonly IDatabase database;

    public NotificationRepository(IDatabase database)
        : base(database)
    {
        this.database = database;
    }

    public IEnumerable<NotificationDefinition> GetEnabled()
    {
        using var connection = this.database.OpenConnection();
        return connection.Query<NotificationDefinition>(
            $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable",
            new { Enable = true });
    }
}
