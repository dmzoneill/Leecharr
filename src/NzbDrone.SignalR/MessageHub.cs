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
    private readonly Logger _logger;

    public MessageHub()
    {
        _logger = LogManager.GetCurrentClassLogger();
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
            Connections.Add(Context.ConnectionId);
        }

        _logger.Debug("SignalR client connected: {0}", Context.ConnectionId);

        var message = new SignalRMessage
        {
            Name = "version",
            Body = new { Version = BuildInfo.Version.ToString() }
        };

        return Clients.Caller.SendAsync("receiveMessage", message);
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        lock (Connections)
        {
            Connections.Remove(Context.ConnectionId);
        }

        _logger.Debug("SignalR client disconnected: {0}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
