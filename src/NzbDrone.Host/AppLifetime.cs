using System;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http.Authentication;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace NzbDrone.Host;

public class AppLifetime : IHostedService, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IDownloadEngine _downloadEngine;
    private readonly ITorrentRepository _torrentRepository;
    private readonly IWatchFolderService _watchFolderService;
    private readonly INetworkSecurityService _networkSecurityService;
    private readonly IRssSyncService _rssSyncService;
    private readonly IDynamicAuthSchemeManager _dynamicAuthManager;
    private readonly Logger _logger;
    private CancellationTokenSource _cts;
    private Task _backgroundLoopTask;

    public AppLifetime(
        IEventAggregator eventAggregator,
        IDownloadEngine downloadEngine,
        ITorrentRepository torrentRepository,
        IWatchFolderService watchFolderService,
        INetworkSecurityService networkSecurityService,
        IRssSyncService rssSyncService,
        IDynamicAuthSchemeManager dynamicAuthManager)
    {
        _eventAggregator = eventAggregator;
        _downloadEngine = downloadEngine;
        _torrentRepository = torrentRepository;
        _watchFolderService = watchFolderService;
        _networkSecurityService = networkSecurityService;
        _rssSyncService = rssSyncService;
        _dynamicAuthManager = dynamicAuthManager;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Leecharr application starting up...");

        try
        {
            await _dynamicAuthManager.InitializeConfiguredProvidersAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error initializing dynamic authentication providers on startup");
        }

        try
        {
            await _downloadEngine.StartAsync();

            var torrents = _torrentRepository.All();
            foreach (var torrent in torrents)
            {
                try
                {
                    await _downloadEngine.AddTorrentAsync(torrent, null);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to restore torrent {0} into engine on startup", torrent.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error initializing download engine on startup");
        }

        _cts = new CancellationTokenSource();
        _backgroundLoopTask = Task.Run(() => RunBackgroundLoopAsync(_cts.Token), _cts.Token);

        _logger.Info("Leecharr application started");
        _eventAggregator.PublishEvent(new ApplicationStartedEvent());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Leecharr application stopping");
        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        try
        {
            if (_backgroundLoopTask != null)
            {
                await Task.WhenAny(_backgroundLoopTask, Task.Delay(3000, cancellationToken));
            }

            await _downloadEngine.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error shutting down download engine");
        }

        _eventAggregator.PublishEvent(new ApplicationShutdownRequested());
    }

    private async Task RunBackgroundLoopAsync(CancellationToken token)
    {
        var rssTickCounter = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, token);

                // 1. Scan watch folder
                await _watchFolderService.ScanWatchFolderAsync();

                // 2. Check VPN Kill Switch
                _networkSecurityService.CheckVpnKillSwitch();

                // 3. RSS Sync every ~15 minutes (90 ticks of 10s)
                rssTickCounter++;
                if (rssTickCounter >= 90)
                {
                    rssTickCounter = 0;
                    await _rssSyncService.SyncRssFeedsAsync();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in background maintenance loop");
            }
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
