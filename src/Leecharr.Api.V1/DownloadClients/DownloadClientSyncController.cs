// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.DownloadClients;

[V1ApiController("downloadclientsync")]
public class DownloadClientSyncController : Controller
{
    private readonly IDownloadClientRepository clientRepository;
    private readonly ITorrentService torrentService;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DownloadClientSyncController(IDownloadClientRepository clientRepository, ITorrentService torrentService, HttpClient httpClient = null)
    {
        this.clientRepository = clientRepository;
        this.torrentService = torrentService;
        this.httpClient = httpClient;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SyncResultResource>> Sync()
    {
        var clients = this.clientRepository.GetEnabled().ToList();
        var syncedCount = 0;
        var totalDiscovered = 0;
        var failedClients = 0;

        foreach (var client in clients)
        {
            try
            {
                var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, this.httpClient);
                totalDiscovered += items.Count;

                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.InfoHash))
                    {
                        continue;
                    }

                    var existing = this.torrentService.GetByInfoHash(item.InfoHash);
                    if (existing == null)
                    {
                        var category = !string.IsNullOrWhiteSpace(item.Category) ? item.Category : client.Category;
                        var savePath = !string.IsNullOrWhiteSpace(item.SavePath) ? item.SavePath : null;
                        var magnetUri = $"magnet:?xt=urn:btih:{item.InfoHash}";

                        await this.torrentService.AddFromMagnetAsync(magnetUri, category, savePath, false);
                        syncedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                failedClients++;
                this.logger.Warn(ex, "Failed to sync download client {0} ({1}:{2})", client.Name, client.Host, client.Port);
            }
        }

        return this.Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = syncedCount,
            TotalCount = totalDiscovered,
            Added = syncedCount,
            Skipped = Math.Max(0, totalDiscovered - syncedCount),
            Failed = failedClients,
            Message = $"Download client sync completed successfully ({syncedCount} torrent(s) imported).",
        });
    }
}
