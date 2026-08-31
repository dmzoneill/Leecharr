// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.SignalR;

public class MessageHub : Hub
{
    private static readonly HashSet<string> Connections = new();
    private readonly Logger logger;

    public MessageHub()
    {
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

    public override Task OnConnectedAsync()
    {
        lock (Connections)
        {
            Connections.Add(this.Context.ConnectionId);
        }

        this.logger.Debug("SignalR client connected: {0}", this.Context.ConnectionId);

        var message = new SignalRMessage
        {
            Name = "version",
            Body = new { Version = BuildInfo.Version.ToString() },
        };

        return this.Clients.Caller.SendAsync("receiveMessage", message);
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        lock (Connections)
        {
            Connections.Remove(this.Context.ConnectionId);
        }

        this.logger.Debug("SignalR client disconnected: {0}", this.Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
