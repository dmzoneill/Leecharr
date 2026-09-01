using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public class DynamicDownloadEngineProxy : IDownloadEngine, ITorrentEngineManager, IDisposable
{
    private readonly IEnumerable<ITorrentEngine> _availableEngines;
    private readonly IConfigService _configService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private ITorrentEngine _activeEngine;
    private bool _disposed;

    public string ProtocolName => Volatile.Read(ref _activeEngine)?.ProtocolName ?? "BitTorrent";
    public ITorrentEngine ActiveEngine => Volatile.Read(ref _activeEngine);
    public string ActiveEngineId => Volatile.Read(ref _activeEngine)?.EngineId ?? "MonoTorrent";

    public DynamicDownloadEngineProxy(
        IEnumerable<ITorrentEngine> availableEngines,
        IConfigService configService,
        ITorrentRepository torrentRepository,
        IEventAggregator eventAggregator)
    {
        _availableEngines = availableEngines;
        _configService = configService;
        _torrentRepository = torrentRepository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        var desiredEngineId = _configService.ActiveTorrentEngine;
        _activeEngine = _availableEngines.FirstOrDefault(e => e.EngineId.Equals(desiredEngineId, StringComparison.OrdinalIgnoreCase))
                        ?? _availableEngines.FirstOrDefault(e => e.EngineId.Equals("MonoTorrent", StringComparison.OrdinalIgnoreCase))
                        ?? _availableEngines.FirstOrDefault();

        if (_activeEngine == null)
        {
            throw new InvalidOperationException("No BitTorrent download engines are registered in the system container.");
        }

        _logger.Info("DynamicDownloadEngineProxy initialized with active engine: {0} ({1})", _activeEngine.DisplayName, _activeEngine.EngineId);
    }

    public IEnumerable<ITorrentEngine> GetEngines()
    {
        return _availableEngines;
    }

    public ITorrentEngine GetEngine(string engineId)
    {
        if (string.IsNullOrWhiteSpace(engineId))
        {
            return null;
        }

        return _availableEngines.FirstOrDefault(e => e.EngineId.Equals(engineId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<EngineHealthCheckResult> ProbeEngineAsync(string engineId)
    {
        var engine = GetEngine(engineId);
        if (engine == null)
        {
            return new EngineHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Engine '{engineId}' is not recognized or registered.",
                Warnings = new List<string> { "Engine identifier not found in active engine registry." }
            };
        }

        return await engine.ProbeHealthAsync();
    }

    public async Task<EngineSwitchResult> SwitchEngineAsync(string targetEngineId, bool preserveTransfers = true)
    {
        if (string.IsNullOrWhiteSpace(targetEngineId))
        {
            return new EngineSwitchResult
            {
                Success = false,
                Error = "Target engine ID must not be empty."
            };
        }

        var targetEngine = GetEngine(targetEngineId);
        if (targetEngine == null)
        {
            return new EngineSwitchResult
            {
                Success = false,
                Error = $"Target engine '{targetEngineId}' is not registered."
            };
        }

        if (string.Equals(Volatile.Read(ref _activeEngine).EngineId, targetEngine.EngineId, StringComparison.OrdinalIgnoreCase))
        {
            return new EngineSwitchResult
            {
                Success = true,
                PreviousEngine = Volatile.Read(ref _activeEngine).EngineId,
                ActiveEngine = targetEngine.EngineId,
                TorrentsMigrated = 0,
                Message = $"Engine '{targetEngine.DisplayName}' is already active."
            };
        }

        await _switchLock.WaitAsync();
        try
        {
            var health = await targetEngine.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new EngineSwitchResult
                {
                    Success = false,
                    PreviousEngine = Volatile.Read(ref _activeEngine).EngineId,
                    ActiveEngine = Volatile.Read(ref _activeEngine).EngineId,
                    Error = $"Cannot switch to engine '{targetEngine.DisplayName}': health check failed ({health.StatusMessage})."
                };
            }

            _logger.Info("Initiating zero-downtime hot-swap: {0} -> {1} (PreserveTransfers: {2})", Volatile.Read(ref _activeEngine).EngineId, targetEngine.EngineId, preserveTransfers);
            var previousEngine = Volatile.Read(ref _activeEngine);
            var rehydrated = 0;

            // 1. Drain and stop previous engine
            _logger.Info("Stopping active engine: {0}...", previousEngine.EngineId);
            try
            {
                await previousEngine.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error while stopping previous engine {0}", previousEngine.EngineId);
            }

            // 2. Start target engine
            _logger.Info("Starting target engine: {0}...", targetEngine.EngineId);
            await targetEngine.StartAsync();

            // 3. Migrate active torrents if requested
            if (preserveTransfers)
            {
                var allTorrents = _torrentRepository.All();
                foreach (var torrent in allTorrents)
                {
                    try
                    {
                        var magnetUri = !string.IsNullOrWhiteSpace(torrent.TrackerUrl)
                            ? $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}"
                            : $"magnet:?xt=urn:btih:{torrent.InfoHash}";

                        await targetEngine.AddTorrentAsync(torrent, null, magnetUri);

                        if (torrent.Status == TorrentStatus.Paused || torrent.Status == TorrentStatus.Stopped)
                        {
                            await targetEngine.PauseTorrentAsync(torrent.Id);
                        }

                        rehydrated++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to rehydrate torrent {0} ({1}) into new engine", torrent.Name, torrent.InfoHash);
                    }
                }
            }

            // 4. Swap active pointer atomically
            Volatile.Write(ref _activeEngine, targetEngine);

            // 5. Persist setting to configuration
            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveTorrentEngine", targetEngine.EngineId }
            });

            _logger.Info("Engine hot-swap completed: {0} -> {1} ({2} torrents migrated)", previousEngine.EngineId, targetEngine.EngineId, rehydrated);

            // 6. Broadcast event
            _eventAggregator.PublishEvent(new TorrentEngineSwitchedEvent(previousEngine.EngineId, targetEngine.EngineId, rehydrated));

            return new EngineSwitchResult
            {
                Success = true,
                PreviousEngine = previousEngine.EngineId,
                ActiveEngine = targetEngine.EngineId,
                TorrentsMigrated = rehydrated,
                Message = $"Successfully switched download engine to {targetEngine.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Fatal error during engine hot-swap to {0}", targetEngineId);
            return new EngineSwitchResult
            {
                Success = false,
                PreviousEngine = Volatile.Read(ref _activeEngine)?.EngineId,
                ActiveEngine = Volatile.Read(ref _activeEngine)?.EngineId,
                Error = $"Hot-swap failed: {ex.Message}"
            };
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public Task StartAsync() => Volatile.Read(ref _activeEngine).StartAsync();
    public Task StopAsync() => Volatile.Read(ref _activeEngine).StopAsync();

    public Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
        => Volatile.Read(ref _activeEngine).AddTorrentAsync(torrent, torrentFileBytes, magnetUri);

    public Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
        => Volatile.Read(ref _activeEngine).RemoveTorrentAsync(torrentId, deleteFiles);

    public Task PauseTorrentAsync(int torrentId)
        => Volatile.Read(ref _activeEngine).PauseTorrentAsync(torrentId);

    public Task ResumeTorrentAsync(int torrentId)
        => Volatile.Read(ref _activeEngine).ResumeTorrentAsync(torrentId);

    public Task ForceRecheckAsync(int torrentId)
        => Volatile.Read(ref _activeEngine).ForceRecheckAsync(torrentId);

    public Task ForceAnnounceAsync(int torrentId)
        => Volatile.Read(ref _activeEngine).ForceAnnounceAsync(torrentId);

    public IDownloadTask GetTask(int torrentId)
        => Volatile.Read(ref _activeEngine).GetTask(torrentId);

    public IEnumerable<IDownloadTask> GetAllTasks()
        => Volatile.Read(ref _activeEngine).GetAllTasks();

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _switchLock.Dispose();
        }
    }
}
