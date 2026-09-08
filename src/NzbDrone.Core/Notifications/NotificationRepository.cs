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
    public NotificationRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<NotificationDefinition> GetEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<NotificationDefinition>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Enable\" = @Enable",
                new { Enable = true }));
    }
}
