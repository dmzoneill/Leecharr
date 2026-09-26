// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.SignalR;

public class NotificationHub : Hub
{
    private static readonly HashSet<string> Connections = new();
    private static readonly ConcurrentDictionary<string, HubCallerContext> ActiveContexts = new();
    private readonly IConfigFileProvider configFileProvider;
    private readonly Logger logger;

    public NotificationHub(IConfigFileProvider configFileProvider = null)
    {
        this.configFileProvider = configFileProvider;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public static bool IsConnected
    {
        get
        {
            lock (Connections)
            {
                return Connections.Count > 0;
            }
        }
    }

    public static int ActiveConnectionCount
    {
        get
        {
            lock (Connections)
            {
                return Connections.Count;
            }
        }
    }

    public static void ResetForTesting()
    {
        lock (Connections)
        {
            Connections.Clear();
        }

        ActiveContexts.Clear();
    }

    public static void AddConnectionForTesting(string connectionId = "test-notification-conn")
    {
        lock (Connections)
        {
            Connections.Add(connectionId);
        }
    }

    public static void RemoveConnectionForTesting(string connectionId = "test-notification-conn")
    {
        lock (Connections)
        {
            Connections.Remove(connectionId);
        }
    }

    public override Task OnConnectedAsync()
    {
        var httpContext = this.Context.GetHttpContext();
        var config = this.configFileProvider ?? (httpContext?.RequestServices?.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider);

        if (this.Context?.User?.Identity?.IsAuthenticated != true && !RpcAuthenticationHelper.IsAuthenticated(httpContext, config))
        {
            this.logger.Warn("Rejecting unauthenticated NotificationHub connection: {0}", this.Context.ConnectionId);
            this.Context.Abort();
            return Task.CompletedTask;
        }

        lock (Connections)
        {
            Connections.Add(this.Context.ConnectionId);
        }

        ActiveContexts[this.Context.ConnectionId] = this.Context;
        this.logger.Debug("NotificationHub client connected: {0}", this.Context.ConnectionId);

        return this.Clients.Caller.SendAsync("notificationConnected", new { ConnectionId = this.Context.ConnectionId });
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        lock (Connections)
        {
            Connections.Remove(this.Context.ConnectionId);
        }

        ActiveContexts.TryRemove(this.Context.ConnectionId, out _);
        this.logger.Debug("NotificationHub client disconnected: {0}", this.Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, $"topic-{topic.ToLowerInvariant()}");
    }

    public async Task UnsubscribeFromTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, $"topic-{topic.ToLowerInvariant()}");
    }

    public async Task SubscribeToLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return;
        }

        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, $"level-{level.ToLowerInvariant()}");
    }

    public async Task UnsubscribeFromLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return;
        }

        await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, $"level-{level.ToLowerInvariant()}");
    }

    public Task DispatchNotification(object notification)
    {
        return this.Clients.All.SendAsync("notificationDispatched", notification);
    }

    public Task DispatchNotificationToTopic(string topic, object notification)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return Task.CompletedTask;
        }

        return this.Clients.Group($"topic-{topic.ToLowerInvariant()}").SendAsync("notificationDispatched", notification);
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

        return this.Clients.All.SendAsync("systemAlert", alert);
    }
}
