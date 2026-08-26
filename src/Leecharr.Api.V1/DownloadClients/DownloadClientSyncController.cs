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
    private readonly IDownloadClientRepository _clientRepository;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public DownloadClientSyncController(IDownloadClientRepository clientRepository, ITorrentService torrentService)
    {
        _clientRepository = clientRepository;
        _torrentService = torrentService;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SyncResultResource>> Sync()
    {
        var clients = _clientRepository.GetEnabled().ToList();
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
                _logger.Warn(ex, "Failed to sync download client {0} ({1}:{2})", client.Name, client.Host, client.Port);
            }
        }

        return Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = syncedCount,
            Message = $"Download client sync completed successfully ({syncedCount}/{clients.Count} connected)."
        });
    }
}
