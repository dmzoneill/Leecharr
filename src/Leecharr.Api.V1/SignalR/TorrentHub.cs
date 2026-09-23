// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using CryptographicOperations = global::System.Security.Cryptography.CryptographicOperations;
using Encoding = global::System.Text.Encoding;

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

        if (config != null && config.AuthenticationEnabled)
        {
            var isAuth = this.Context.User?.Identity?.IsAuthenticated == true;
            if (!isAuth && httpContext != null)
            {
                var masterApiKey = config.ApiKey;
                if (!string.IsNullOrWhiteSpace(masterApiKey))
                {
                    if (httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
                    {
                        var authStr = authHeader.ToString().Trim();
                        if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = authStr["Bearer ".Length..].Trim();
                            if (FixedTimeEquals(token, masterApiKey))
                            {
                                isAuth = true;
                            }
                        }
                        else if (authStr.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                        {
                            var param = authStr["Basic ".Length..].Trim();
                            try
                            {
                                var credentialBytes = Convert.FromBase64String(param);
                                var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
                                var username = credentials.Length > 0 ? credentials[0] : string.Empty;
                                var password = credentials.Length > 1 ? credentials[1] : string.Empty;

                                if (FixedTimeEquals(password, masterApiKey) || FixedTimeEquals(username, masterApiKey))
                                {
                                    isAuth = true;
                                }
                                else
                                {
                                    var userService = httpContext.RequestServices?.GetService(typeof(NzbDrone.Core.Authentication.IUserService)) as NzbDrone.Core.Authentication.IUserService;
                                    if (userService != null && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                                    {
                                        var user = userService.Authenticate(username, password);
                                        if (user != null)
                                        {
                                            isAuth = true;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore malformed Basic auth header
                            }
                        }
                    }

                    if (!isAuth && httpContext.Request.Headers.TryGetValue("X-Api-Key", out var headerKey) &&
                        FixedTimeEquals(headerKey.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Headers.TryGetValue("ApiKey", out var customApiKey) &&
                        FixedTimeEquals(customApiKey.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("access_token", out var queryToken) &&
                        FixedTimeEquals(queryToken.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("apikey", out var queryApiKey) &&
                        FixedTimeEquals(queryApiKey.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("api_key", out var queryApiKey2) &&
                        FixedTimeEquals(queryApiKey2.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("token", out var queryToken2) &&
                        FixedTimeEquals(queryToken2.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                }
            }

            if (!isAuth)
            {
                this.logger.Warn("Rejecting unauthenticated TorrentHub connection: {0}", this.Context.ConnectionId);
                this.Context.Abort();
                return Task.CompletedTask;
            }
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

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }
}
