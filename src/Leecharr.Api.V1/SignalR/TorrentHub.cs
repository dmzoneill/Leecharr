// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.SignalR;

public class TorrentHub : Hub
{
    private static readonly HashSet<string> Connections = new();
    private static readonly ConcurrentDictionary<string, HubCallerContext> ActiveContexts = new();
    private readonly IConfigFileProvider configFileProvider;
    private readonly ITorrentService torrentService;
    private readonly Logger logger;

    public TorrentHub(IConfigFileProvider configFileProvider = null, ITorrentService torrentService = null)
    {
        this.configFileProvider = configFileProvider;
        this.torrentService = torrentService;
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

    public static void AddConnectionForTesting(string connectionId = "test-torrent-conn")
    {
        lock (Connections)
        {
            Connections.Add(connectionId);
        }
    }

    public static void RemoveConnectionForTesting(string connectionId = "test-torrent-conn")
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

        if (!RpcAuthenticationHelper.IsAuthenticated(httpContext, config))
        {
            this.logger.Warn("Rejecting unauthenticated TorrentHub connection: {0}", this.Context.ConnectionId);
            this.Context.Abort();
            return Task.CompletedTask;
        }

        lock (Connections)
        {
            Connections.Add(this.Context.ConnectionId);
        }

        ActiveContexts[this.Context.ConnectionId] = this.Context;
        this.logger.Debug("TorrentHub client connected: {0}", this.Context.ConnectionId);

        return this.Clients.Caller.SendAsync("torrentConnected", new { ConnectionId = this.Context.ConnectionId });
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        lock (Connections)
        {
            Connections.Remove(this.Context.ConnectionId);
        }

        ActiveContexts.TryRemove(this.Context.ConnectionId, out _);
        this.logger.Debug("TorrentHub client disconnected: {0}", this.Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToTorrent(int torrentId)
    {
        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, $"torrent-{torrentId}");
    }

    public async Task UnsubscribeFromTorrent(int torrentId)
    {
        await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, $"torrent-{torrentId}");
    }

    public async Task SubscribeToAllTorrents()
    {
        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, "torrents");
    }

    public async Task UnsubscribeFromAllTorrents()
    {
        await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, "torrents");
    }

    public Task BroadcastTorrentProgress(int torrentId, object progressPayload)
    {
        return this.Clients.Group($"torrent-{torrentId}").SendAsync("torrentProgress", progressPayload);
    }

    public Task BroadcastGlobalProgress(object progressPayload)
    {
        return this.Clients.Group("torrents").SendAsync("torrentProgress", progressPayload);
    }

    public Task PushSwarmTelemetry(int torrentId, object swarmData)
    {
        return this.Clients.Group($"torrent-{torrentId}").SendAsync("swarmTelemetry", swarmData);
    }

    public Task BroadcastSwarmTelemetry(object swarmData)
    {
        return this.Clients.All.SendAsync("swarmTelemetry", swarmData);
    }
}
