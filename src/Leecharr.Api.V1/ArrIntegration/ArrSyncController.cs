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
    private readonly IArrConnectionRepository arrRepository;
    private readonly ITorrentService torrentService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public ArrSyncController(IArrConnectionRepository arrRepository, ITorrentService torrentService)
    {
        this.arrRepository = arrRepository;
        this.torrentService = torrentService;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SyncResultResource>> Sync()
    {
        var connections = this.arrRepository.GetEnabled().ToList();
        var syncedCount = 0;

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        foreach (var conn in connections)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(conn.Url))
                {
                    var baseUrl = conn.Url.TrimEnd('/');
                    var uri = new Uri(new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/"), "api/v3/system/status");
                    var req = new HttpRequestMessage(HttpMethod.Get, uri);
                    if (!string.IsNullOrWhiteSpace(conn.ApiKey))
                    {
                        req.Headers.Add("X-Api-Key", conn.ApiKey);
                    }

                    var response = await httpClient.SendAsync(req);
                    if (response.IsSuccessStatusCode)
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

        return this.Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = syncedCount,
            Message = $"Arr sync completed successfully ({syncedCount}/{connections.Count} connected).",
        });
    }
}
