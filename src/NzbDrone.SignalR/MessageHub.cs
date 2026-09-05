// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.SignalR;

public class MessageHub : Hub
{
    private static readonly HashSet<string> Connections = new();
    private readonly IConfigFileProvider configFileProvider;
    private readonly Logger logger;

    public MessageHub(IConfigFileProvider configFileProvider = null)
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
                    if (httpContext.Request.Headers.TryGetValue("X-Api-Key", out var headerKey) &&
                        string.Equals(headerKey.ToString(), masterApiKey, StringComparison.Ordinal))
                    {
                        isAuth = true;
                    }
                    else if (httpContext.Request.Headers.TryGetValue("ApiKey", out var customApiKey) &&
                             string.Equals(customApiKey.ToString(), masterApiKey, StringComparison.Ordinal))
                    {
                        isAuth = true;
                    }
                    else if (httpContext.Request.Query.TryGetValue("access_token", out var queryToken) &&
                             string.Equals(queryToken.ToString(), masterApiKey, StringComparison.Ordinal))
                    {
                        isAuth = true;
                    }
                    else if (httpContext.Request.Query.TryGetValue("apikey", out var queryApiKey) &&
                             string.Equals(queryApiKey.ToString(), masterApiKey, StringComparison.Ordinal))
                    {
                        isAuth = true;
                    }
                    else if (httpContext.Request.Query.TryGetValue("api_key", out var queryApiKey2) &&
                             string.Equals(queryApiKey2.ToString(), masterApiKey, StringComparison.Ordinal))
                    {
                        isAuth = true;
                    }
                    else if (httpContext.Request.Query.TryGetValue("token", out var queryToken2) &&
                             string.Equals(queryToken2.ToString(), masterApiKey, StringComparison.Ordinal))
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
