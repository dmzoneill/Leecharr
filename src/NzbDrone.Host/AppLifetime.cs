using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http.Authentication;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace NzbDrone.Host;

public class AppLifetime : IHostedService, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IDownloadEngine _downloadEngine;
    private readonly ITorrentRepository _torrentRepository;
    private readonly IWatchFolderService _watchFolderService;
    private readonly INetworkSecurityService _networkSecurityService;
    private readonly IRssSyncService _rssSyncService;
    private readonly IDynamicAuthSchemeManager _dynamicAuthManager;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;
    private CancellationTokenSource _cts;
    private Task _backgroundLoopTask;

    public AppLifetime(
        IConfigService configService,
        IEventAggregator eventAggregator,
        IDownloadEngine downloadEngine,
        ITorrentRepository torrentRepository,
        IWatchFolderService watchFolderService,
        INetworkSecurityService networkSecurityService,
        IRssSyncService rssSyncService,
        IDynamicAuthSchemeManager dynamicAuthManager,
        ITorrentService torrentService = null)
    {
        _configService = configService;
        _eventAggregator = eventAggregator;
        _downloadEngine = downloadEngine;
        _torrentRepository = torrentRepository;
        _watchFolderService = watchFolderService;
        _networkSecurityService = networkSecurityService;
        _rssSyncService = rssSyncService;
        _dynamicAuthManager = dynamicAuthManager;
        _torrentService = torrentService;
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

            if (_configService.AutoStart)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var torrentsDir1 = Path.Combine(appData, "Torrents");
                var torrentsDir2 = Path.Combine(appData, "Leecharr", "Torrents");
                var torrents = _torrentRepository.All();

                foreach (var torrent in torrents)
                {
                    try
                    {
                        byte[] fileBytes = null;
                        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
                        {
                            var hash = torrent.InfoHash.ToLowerInvariant();
                            var path1 = Path.Combine(torrentsDir1, $"{hash}.torrent");
                            var path2 = Path.Combine(torrentsDir2, $"{hash}.torrent");
                            if (File.Exists(path1))
                            {
                                fileBytes = await File.ReadAllBytesAsync(path1, cancellationToken);
                            }
                            else if (File.Exists(path2))
                            {
                                fileBytes = await File.ReadAllBytesAsync(path2, cancellationToken);
                            }
                        }

                        await _downloadEngine.AddTorrentAsync(torrent, fileBytes);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to restore torrent {0} into engine on startup", torrent.Name);
                    }
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
        _logger.Info("Leecharr application shutting down...");

        try
        {
            if (_cts != null)
            {
                await _cts.CancelAsync();
            }

            if (_backgroundLoopTask != null)
            {
                await Task.WhenAny(_backgroundLoopTask, Task.Delay(5000, cancellationToken));
            }
        }
        catch
        {
        }

        try
        {
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
        var watchFolderTickCounter = 0;
        var rssTickCounter = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, token);

                // Automated seeding check
                if (_torrentService != null)
                {
                    try
                    {
                        var torrents = _torrentService.GetAll();
                        foreach (var torrent in torrents)
                        {
                            if (torrent.Status == TorrentStatus.Seeding)
                            {
                                var ratioReached = torrent.TargetRatio > 0 && torrent.Ratio >= torrent.TargetRatio;
                                var timeReached = torrent.TargetSeedTimeMinutes > 0 && torrent.SeedTimeMinutes >= torrent.TargetSeedTimeMinutes;

                                if (ratioReached || timeReached)
                                {
                                    _logger.Info("Torrent {0} reached seed goal (Ratio: {1:F2}/{2:F2}, SeedTime: {3}/{4}m). Pausing seeding.", torrent.Name, torrent.Ratio, torrent.TargetRatio, torrent.SeedTimeMinutes, torrent.TargetSeedTimeMinutes);

                                    var oldStatus = torrent.Status;
                                    await _torrentService.PauseAsync(torrent.Id);
                                    _eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                                    {
                                        Torrent = torrent,
                                        OldStatus = oldStatus,
                                        NewStatus = TorrentStatus.Stopped
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Error checking seed goals in background loop");
                    }
                }

                // 1. Scan watch folder according to configured interval
                watchFolderTickCounter++;
                var watchInterval = _configService.WatchFolderScanIntervalSeconds > 0
                    ? _configService.WatchFolderScanIntervalSeconds
                    : 10;

                if (watchFolderTickCounter >= watchInterval)
                {
                    watchFolderTickCounter = 0;
                    await _watchFolderService.ScanWatchFolderAsync();
                }

                // 2. Check VPN Kill Switch every 5 seconds
                if (watchFolderTickCounter % 5 == 0)
                {
                    _networkSecurityService.CheckVpnKillSwitch();
                }

                // 3. RSS Sync every ~15 minutes (900 seconds)
                rssTickCounter++;
                if (rssTickCounter >= 900)
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
