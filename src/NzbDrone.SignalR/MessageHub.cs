// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.SignalR;

public class MessageHub : Hub
{
    private static readonly HashSet<string> Connections = new();
    private static readonly ConcurrentDictionary<string, HubCallerContext> ActiveContexts = new();
    private readonly IConfigFileProvider configFileProvider;
    private readonly ITorrentService torrentService;
    private readonly Logger logger;

    public MessageHub(IConfigFileProvider configFileProvider = null, ITorrentService torrentService = null)
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

    public static void ResetForTesting()
    {
        lock (Connections)
        {
            Connections.Clear();
        }

        ActiveContexts.Clear();
    }

    public static void DisconnectAllConnections()
    {
        lock (Connections)
        {
            Connections.Clear();
        }

        foreach (var kvp in ActiveContexts)
        {
            try
            {
                kvp.Value.Abort();
            }
            catch
            {
                // Suppress abort failures
            }
        }

        ActiveContexts.Clear();
    }

    public static void AddConnectionForTesting(string connectionId = "test-connection")
    {
        lock (Connections)
        {
            Connections.Add(connectionId);
        }
    }

    public static void RemoveConnectionForTesting(string connectionId = "test-connection")
    {
        lock (Connections)
        {
            Connections.Remove(connectionId);
        }
    }

    public override Task OnConnectedAsync()
    {
        var httpContext = this.Context.GetHttpContext();
        var config = this.configFileProvider ?? (httpContext?.RequestServices.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider);

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
                                var credentials = System.Text.Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
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
                this.logger.Warn("Rejecting unauthenticated SignalR connection: {0}", this.Context.ConnectionId);
                this.Context.Abort();
                return Task.CompletedTask;
            }
        }

        lock (Connections)
        {
            Connections.Add(this.Context.ConnectionId);
        }

        ActiveContexts[this.Context.ConnectionId] = this.Context;

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

        ActiveContexts.TryRemove(this.Context.ConnectionId, out _);

        this.logger.Debug("SignalR client disconnected: {0}", this.Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
    }

    public Task TrackerUpdated(object payload)
    {
        return this.Clients.All.SendAsync("trackerUpdated", payload);
    }

    public Task TrackerAnnounced(object payload)
    {
        return this.Clients.All.SendAsync("trackerAnnounced", payload);
    }

    public async Task SubscribeToTorrent(int torrentId)
    {
        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, $"torrent-{torrentId}");
    }

    public async Task UnsubscribeFromTorrent(int torrentId)
    {
        await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, $"torrent-{torrentId}");
    }

    public async Task SubscribeToChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, $"channel-{channel.ToLowerInvariant()}");
    }

    public async Task UnsubscribeFromChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, $"channel-{channel.ToLowerInvariant()}");
    }

    public virtual async Task<StateSnapshotResource> RequestStateSnapshot()
    {
        var httpContext = this.Context.GetHttpContext();
        var svc = this.torrentService ?? (httpContext?.RequestServices?.GetService(typeof(ITorrentService)) as ITorrentService);

        var torrents = (svc?.GetAll() ?? Enumerable.Empty<Torrent>()).ToList();

        long totalDownloadSpeed = 0;
        long totalUploadSpeed = 0;
        var summaries = new List<TorrentSnapshotResource>(torrents.Count);

        foreach (var t in torrents)
        {
            totalDownloadSpeed += t.DownloadSpeed;
            totalUploadSpeed += t.UploadSpeed;

            var isActive = t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding;

            summaries.Add(new TorrentSnapshotResource
            {
                Id = t.Id,
                Name = t.Name,
                Status = t.Status.ToString(),
                Progress = t.Progress,
                DownloadSpeed = t.DownloadSpeed,
                UploadSpeed = t.UploadSpeed,
                Eta = t.Eta,
                Size = t.TotalSize,
                TotalSize = t.TotalSize,
                Active = isActive,
            });
        }

        var snapshot = new StateSnapshotResource
        {
            Torrents = summaries,
            DownloadSpeed = totalDownloadSpeed,
            UploadSpeed = totalUploadSpeed,
            ActiveCount = torrents.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding),
            TotalCount = torrents.Count,
            TimestampUtc = DateTime.UtcNow,
        };

        if (this.Clients?.Caller != null)
        {
            try
            {
                await this.Clients.Caller.SendAsync("stateSnapshot", snapshot);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to send stateSnapshot event to caller");
            }
        }

        return snapshot;
    }
}
