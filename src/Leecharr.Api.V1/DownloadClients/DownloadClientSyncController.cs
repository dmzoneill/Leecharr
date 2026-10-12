// Copyright (c) FeedItOut. All rights reserved.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.DownloadClients;

[V1ApiController("downloadclientsync")]
[Authorize(Policy = "RequireOperator")]
public class DownloadClientSyncController : Controller
{
    private static readonly SemaphoreSlim SyncSemaphore = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> InFlightInfoHashes = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDownloadClientRepository clientRepository;
    private readonly ITorrentService torrentService;
    private readonly HttpClient httpClient;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IDataProtector protector;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DownloadClientSyncController(
        IDownloadClientRepository clientRepository,
        ITorrentService torrentService,
        HttpClient httpClient = null,
        ISafeHttpClientService safeHttpClientService = null,
        IDataProtectionProvider dataProtectionProvider = null)
    {
        this.clientRepository = clientRepository;
        this.torrentService = torrentService;
        this.httpClient = httpClient;
        this.safeHttpClientService = safeHttpClientService;
        this.protector = dataProtectionProvider?.CreateProtector("DownloadClient.Password");
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
            var failedTorrents = 0;
            foreach (var client in clients)
            {
                try
                {
                    var queryResult = await DownloadClientRemoteQuery.QueryRemoteClientItemsDetailedAsync(client, this.httpClient, this.safeHttpClientService, protector: this.protector);
                    if (!queryResult.IsSuccess)
                    {
                        failedClients++;
                        this.logger.Warn(
                            "Failed to sync download client {0} ({1}:{2}): {3}",
                            client.Name,
                            client.Host,
                            client.Port,
                            queryResult.ErrorMessage);
                        continue;
                    }

                    var items = queryResult.Items;
                    totalDiscovered += items.Count;

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.InfoHash))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(client.Category) && !DownloadClientRemoteQuery.MatchesCategory(item, client.Category))
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
                                if (!DownloadClientRemoteImportHelper.TryBuildMagnetUri(item.InfoHash, item.Name, out var magnetUri, out var magnetError))
                                {
                                    failedTorrents++;
                                    this.logger.Warn(
                                        "Failed to build magnet for torrent {0} from download client {1}: {2}",
                                        item.InfoHash,
                                        client.Name,
                                        magnetError);
                                    continue;
                                }

                                var startPaused = DownloadClientRemoteImportHelper.ShouldStartPaused(item.State);
                                await this.torrentService.AddFromMagnetAsync(magnetUri, category, savePath, startPaused);
                                syncedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failedTorrents++;
                            this.logger.Warn(ex, "Failed to import torrent {0} from download client {1}", item.InfoHash, client.Name);
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

            var success = failedClients == 0;
            var message = success
                ? $"Download client sync completed successfully ({syncedCount} torrent(s) imported)."
                : $"Download client sync completed with {failedClients} client failure(s) ({syncedCount} torrent(s) imported).";

            return this.Ok(new SyncResultResource
            {
                Success = success,
                SyncedCount = syncedCount,
                TotalCount = totalDiscovered,
                Added = syncedCount,
                Skipped = Math.Max(0, totalDiscovered - syncedCount - failedTorrents),
                FailedCount = failedTorrents,
                Failed = failedClients,
                Message = message,
            });
        }
        finally
        {
            SyncSemaphore.Release();
        }
    }
}
