// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.ArrIntegration;

[V1ApiController("arrsync")]
public class ArrSyncController : Controller
{
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IArrConnectionRepository arrRepository;
    private readonly ITorrentService torrentService;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public ArrSyncController(IArrConnectionRepository arrRepository, ITorrentService torrentService, HttpClient httpClient = null)
    {
        this.arrRepository = arrRepository;
        this.torrentService = torrentService;
        this.httpClient = httpClient;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SyncResultResource>> Sync()
    {
        var connections = this.arrRepository.GetEnabled().ToList();
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

                        var response = await client.SendAsync(req);
                        if (response.IsSuccessStatusCode)
                        {
                            connected = true;
                            break;
                        }
                    }

                    if (connected)
                    {
                        syncedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to sync Arr connection {0} ({1})", conn.Name, conn.Url);
            }
        }

        var failedCount = connections.Count - syncedCount;
        return this.Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = syncedCount,
            TotalCount = connections.Count,
            FailedCount = failedCount,
            Added = syncedCount,
            Skipped = 0,
            Failed = failedCount,
            Message = $"Arr sync completed successfully ({syncedCount}/{connections.Count} connected).",
        });
    }
}
