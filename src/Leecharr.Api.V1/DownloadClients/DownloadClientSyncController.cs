// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.DownloadClients;

[V1ApiController("downloadclientsync")]
public class DownloadClientSyncController : Controller
{
    private static readonly SemaphoreSlim SyncSemaphore = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> InFlightInfoHashes = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDownloadClientRepository clientRepository;
    private readonly ITorrentService torrentService;
    private readonly HttpClient httpClient;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DownloadClientSyncController(
        IDownloadClientRepository clientRepository,
        ITorrentService torrentService,
        HttpClient httpClient = null,
        IHttpClientFactory httpClientFactory = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.clientRepository = clientRepository;
        this.torrentService = torrentService;
        this.httpClient = httpClient;
        this.httpClientFactory = httpClientFactory;
        this.safeHttpClientService = safeHttpClientService;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SyncResultResource>> Sync()
    {
        await SyncSemaphore.WaitAsync();
        try
        {
            var clients = this.clientRepository.GetEnabled().ToList();
            var syncedCount = 0;
            var totalDiscovered = 0;
            var failedClients = 0;
            var http = this.httpClient ?? this.httpClientFactory?.CreateClient();

            foreach (var client in clients)
            {
                try
                {
                    var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, http, this.safeHttpClientService);
                    totalDiscovered += items.Count;

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.InfoHash))
                        {
                            continue;
                        }

                        if (!InFlightInfoHashes.TryAdd(item.InfoHash, 0))
                        {
                            continue;
                        }

                        try
                        {
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
                        finally
                        {
                            InFlightInfoHashes.TryRemove(item.InfoHash, out _);
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
        finally
        {
            SyncSemaphore.Release();
        }
    }
}
