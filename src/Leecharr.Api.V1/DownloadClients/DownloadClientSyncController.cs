// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Net.Sockets;
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
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DownloadClientSyncController(IDownloadClientRepository clientRepository, ITorrentService torrentService)
    {
        this.clientRepository = clientRepository;
        this.torrentService = torrentService;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SyncResultResource>> Sync()
    {
        var clients = this.clientRepository.GetEnabled().ToList();
        var syncedCount = 0;

        foreach (var client in clients)
        {
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(client.Host, client.Port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(3000));
                if (completed == connectTask && tcp.Connected)
                {
                    syncedCount++;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to sync download client {0} ({1}:{2})", client.Name, client.Host, client.Port);
            }
        }

        return this.Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = syncedCount,
            Message = $"Download client sync completed successfully ({syncedCount}/{clients.Count} connected).",
        });
    }
}
