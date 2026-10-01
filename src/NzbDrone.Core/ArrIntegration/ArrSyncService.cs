// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.ArrIntegration;

public class ArrSyncService : IArrSyncService, IExecute<SyncArrCommand>, IExecuteAsync<SyncArrCommand>
{
    private static readonly HttpClient DefaultHttpClient = new(new SocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly IArrConnectionRepository arrRepository;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public ArrSyncService(IArrConnectionRepository arrRepository, HttpClient httpClient = null)
    {
        this.arrRepository = arrRepository;
        this.httpClient = httpClient;
    }

    public async Task ExecuteAsync(SyncArrCommand message, CancellationToken cancellationToken = default)
    {
        await this.SyncAsync(message?.AppType, message?.InstanceId, cancellationToken);
    }

    public void Execute(SyncArrCommand message)
    {
        this.Sync(message?.AppType, message?.InstanceId);
    }

    public int Sync(string appType = null, int? instanceId = null)
    {
        return this.SyncAsync(appType, instanceId).GetAwaiter().GetResult();
    }

    public async Task<int> SyncAsync(string appType = null, int? instanceId = null, CancellationToken cancellationToken = default)
    {
        var connections = this.arrRepository.GetEnabled().ToList();

        if (instanceId.HasValue)
        {
            connections = connections.Where(c => c.Id == instanceId.Value).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(appType))
        {
            connections = connections.Where(c => string.Equals(c.ArrType, appType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var syncedCount = 0;
        var client = this.httpClient ?? DefaultHttpClient;

        foreach (var conn in connections)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(conn.Url))
                {
                    var baseUrl = conn.Url.TrimEnd('/');
                    var endpoints = string.Equals(conn.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(conn.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(conn.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase)
                        ? new[] { "api/v1/system/status", "api/v3/system/status" }
                        : new[] { "api/v3/system/status", "api/v1/system/status" };

                    var connected = false;

                    foreach (var endpoint in endpoints)
                    {
                        var uri = new Uri(new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/"), endpoint);
                        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                        if (!string.IsNullOrWhiteSpace(conn.ApiKey))
                        {
                            req.Headers.Add("X-Api-Key", conn.ApiKey);
                        }

                        using var response = await client.SendAsync(req, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            connected = true;
                            break;
                        }
                    }

                    if (connected)
                    {
                        syncedCount++;
                        this.logger.Info("Successfully synced Arr connection {0} ({1})", conn.Name, conn.Url);
                    }
                    else
                    {
                        this.logger.Warn("Failed to sync Arr connection {0} ({1}): No endpoint responded successfully", conn.Name, conn.Url);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to sync Arr connection {0} ({1})", conn.Name, conn.Url);
            }
        }

        return syncedCount;
    }
}
