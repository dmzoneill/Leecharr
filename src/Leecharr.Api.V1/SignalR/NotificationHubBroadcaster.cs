// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Leecharr.Api.V1.SignalR;

public interface INotificationHubBroadcaster
{
    Task DispatchNotification(object notification);

    Task DispatchNotificationToTopic(string topic, object notification);

    Task DispatchSystemAlert(string alertType, string message, string severity);
}

public class NotificationHubBroadcaster : INotificationHubBroadcaster
{
    private readonly IHubContext<NotificationHub> hubContext;

    public NotificationHubBroadcaster(IHubContext<NotificationHub> hubContext)
    {
        this.hubContext = hubContext;
    }

    public Task DispatchNotification(object notification)
    {
        return this.hubContext.Clients.All.SendAsync("notificationDispatched", notification);
    }

    public Task DispatchNotificationToTopic(string topic, object notification)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return Task.CompletedTask;
        }

        return this.hubContext.Clients.Group($"topic-{topic.ToLowerInvariant()}").SendAsync("notificationDispatched", notification);
    }

    public Task DispatchSystemAlert(string alertType, string message, string severity)
    {
        var alert = new
        {
            Type = alertType,
            Message = message,
            Severity = severity,
            Timestamp = DateTime.UtcNow,
        };

        return this.hubContext.Clients.All.SendAsync("systemAlert", alert);
    }
}
