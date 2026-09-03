// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IEnumerable<ITorrentEngine> availableEngines;
    private readonly IConfigService configService;
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentFileRepository torrentFileRepository;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly SemaphoreSlim switchLock = new(1, 1);
    private ITorrentEngine activeEngine;
    private bool disposed;

    public string ProtocolName => Volatile.Read(ref this.activeEngine)?.ProtocolName ?? "BitTorrent";

    public ITorrentEngine ActiveEngine => Volatile.Read(ref this.activeEngine);

    public string ActiveEngineId => Volatile.Read(ref this.activeEngine)?.EngineId ?? "MonoTorrent";

    public int DhtNodeCount => Volatile.Read(ref this.activeEngine)?.DhtNodeCount ?? 0;

    public DynamicDownloadEngineProxy(
        IEnumerable<ITorrentEngine> availableEngines,
        IConfigService configService,
        ITorrentRepository torrentRepository,
        IEventAggregator eventAggregator,
        ITorrentFileRepository torrentFileRepository = null)
    {
        this.availableEngines = availableEngines;
        this.configService = configService;
        this.torrentRepository = torrentRepository;
        this.torrentFileRepository = torrentFileRepository;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredEngineId = this.configService.ActiveTorrentEngine;
        this.activeEngine = this.availableEngines.FirstOrDefault(e => e.EngineId.Equals(desiredEngineId, StringComparison.OrdinalIgnoreCase))
                        ?? this.availableEngines.FirstOrDefault(e => e.EngineId.Equals("MonoTorrent", StringComparison.OrdinalIgnoreCase))
                        ?? this.availableEngines.FirstOrDefault();

        if (this.activeEngine == null)
        {
            throw new InvalidOperationException("No BitTorrent download engines are registered in the system container.");
        }

        this.logger.Info("DynamicDownloadEngineProxy initialized with active engine: {0} ({1})", this.activeEngine.DisplayName, this.activeEngine.EngineId);
    }

    public IEnumerable<ITorrentEngine> GetEngines()
    {
        return this.availableEngines;
    }

    public ITorrentEngine GetEngine(string engineId)
    {
        if (string.IsNullOrWhiteSpace(engineId))
        {
            return null;
        }

        return this.availableEngines.FirstOrDefault(e => e.EngineId.Equals(engineId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<EngineHealthCheckResult> ProbeEngineAsync(string engineId)
    {
        var engine = this.GetEngine(engineId);
        if (engine == null)
        {
            return new EngineHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Engine '{engineId}' is not recognized or registered.",
                Warnings = new List<string> { "Engine identifier not found in active engine registry." },
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
                Error = "Target engine ID must not be empty.",
            };
        }

        var targetEngine = this.GetEngine(targetEngineId);
        if (targetEngine == null)
        {
            return new EngineSwitchResult
            {
                Success = false,
                Error = $"Target engine '{targetEngineId}' is not registered.",
            };
        }

        if (string.Equals(Volatile.Read(ref this.activeEngine).EngineId, targetEngine.EngineId, StringComparison.OrdinalIgnoreCase))
        {
            return new EngineSwitchResult
            {
                Success = true,
                PreviousEngine = Volatile.Read(ref this.activeEngine).EngineId,
                ActiveEngine = targetEngine.EngineId,
                TorrentsMigrated = 0,
                Message = $"Engine '{targetEngine.DisplayName}' is already active.",
            };
        }

        await this.switchLock.WaitAsync();
        try
        {
            var health = await targetEngine.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new EngineSwitchResult
                {
                    Success = false,
                    PreviousEngine = Volatile.Read(ref this.activeEngine).EngineId,
                    ActiveEngine = Volatile.Read(ref this.activeEngine).EngineId,
                    Error = $"Cannot switch to engine '{targetEngine.DisplayName}': health check failed ({health.StatusMessage}).",
                };
            }

            this.logger.Info("Initiating zero-downtime hot-swap: {0} -> {1} (PreserveTransfers: {2})", Volatile.Read(ref this.activeEngine).EngineId, targetEngine.EngineId, preserveTransfers);
            var previousEngine = Volatile.Read(ref this.activeEngine);
            var rehydrated = 0;

            // 1. Drain and stop previous engine
            this.logger.Info("Stopping active engine: {0}...", previousEngine.EngineId);
            try
            {
                await previousEngine.StopAsync();
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error while stopping previous engine {0}", previousEngine.EngineId);
            }

            // 2. Start target engine
            this.logger.Info("Starting target engine: {0}...", targetEngine.EngineId);
            await targetEngine.StartAsync();

            // 3. Migrate active torrents if requested
            if (preserveTransfers)
            {
                var allTorrents = this.torrentRepository.All();
                foreach (var torrent in allTorrents)
                {
                    try
                    {
                        var magnetUri = !string.IsNullOrWhiteSpace(torrent.TrackerUrl)
                            ? $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}"
                            : $"magnet:?xt=urn:btih:{torrent.InfoHash}";

                        await targetEngine.AddTorrentAsync(torrent, null, magnetUri);

                        if (this.torrentFileRepository != null)
                        {
                            var files = this.torrentFileRepository.GetByTorrentId(torrent.Id);
                            foreach (var file in files)
                            {
                                if (file.Priority != 1)
                                {
                                    await targetEngine.SetFilePriorityAsync(torrent.Id, file.Path, file.Priority);
                                }
                            }
                        }

                        if (torrent.Status == TorrentStatus.Paused || torrent.Status == TorrentStatus.Stopped)
                        {
                            await targetEngine.PauseTorrentAsync(torrent.Id);
                        }

                        rehydrated++;
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to rehydrate torrent {0} ({1}) into new engine", torrent.Name, torrent.InfoHash);
                    }
                }
            }

            // 4. Swap active pointer atomically
            Volatile.Write(ref this.activeEngine, targetEngine);

            // 5. Persist setting to configuration
            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveTorrentEngine", targetEngine.EngineId },
            });

            this.logger.Info("Engine hot-swap completed: {0} -> {1} ({2} torrents migrated)", previousEngine.EngineId, targetEngine.EngineId, rehydrated);

            // 6. Broadcast event
            this.eventAggregator.PublishEvent(new TorrentEngineSwitchedEvent(previousEngine.EngineId, targetEngine.EngineId, rehydrated));

            return new EngineSwitchResult
            {
                Success = true,
                PreviousEngine = previousEngine.EngineId,
                ActiveEngine = targetEngine.EngineId,
                TorrentsMigrated = rehydrated,
                Message = $"Successfully switched download engine to {targetEngine.DisplayName}.",
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Fatal error during engine hot-swap to {0}", targetEngineId);
            return new EngineSwitchResult
            {
                Success = false,
                PreviousEngine = Volatile.Read(ref this.activeEngine)?.EngineId,
                ActiveEngine = Volatile.Read(ref this.activeEngine)?.EngineId,
                Error = $"Hot-swap failed: {ex.Message}",
            };
        }
        finally
        {
            this.switchLock.Release();
        }
    }

    public bool IsHaltedByKillSwitch => Volatile.Read(ref this.activeEngine)?.IsHaltedByKillSwitch ?? false;

    public Task StartAsync() => Volatile.Read(ref this.activeEngine).StartAsync();

    public Task StopAsync() => Volatile.Read(ref this.activeEngine).StopAsync();

    public Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
        => Volatile.Read(ref this.activeEngine).AddTorrentAsync(torrent, torrentFileBytes, magnetUri);

    public Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
        => Volatile.Read(ref this.activeEngine).RemoveTorrentAsync(torrentId, deleteFiles);

    public Task PauseTorrentAsync(int torrentId)
        => Volatile.Read(ref this.activeEngine).PauseTorrentAsync(torrentId);

    public Task ResumeTorrentAsync(int torrentId)
        => Volatile.Read(ref this.activeEngine).ResumeTorrentAsync(torrentId);

    public Task ForceRecheckAsync(int torrentId)
        => Volatile.Read(ref this.activeEngine).ForceRecheckAsync(torrentId);

    public Task ForceAnnounceAsync(int torrentId)
        => Volatile.Read(ref this.activeEngine).ForceAnnounceAsync(torrentId);

    public Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
        => Volatile.Read(ref this.activeEngine).AddTrackersAsync(torrentId, trackers);

    public Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
        => Volatile.Read(ref this.activeEngine).SetFilePriorityAsync(torrentId, filePath, priority);

    public Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
        => Volatile.Read(ref this.activeEngine).SetRateLimitsAsync(maxDownloadKbps, maxUploadKbps);

    public Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps)
        => Volatile.Read(ref this.activeEngine).SetTorrentRateLimitsAsync(torrentId, maxDownloadKbps, maxUploadKbps);

    public IDownloadTask GetTask(int torrentId)
        => Volatile.Read(ref this.activeEngine).GetTask(torrentId);

    public IEnumerable<IDownloadTask> GetAllTasks()
        => Volatile.Read(ref this.activeEngine).GetAllTasks();

    public TorrentEngineMetrics GetEngineMetrics()
        => Volatile.Read(ref this.activeEngine)?.GetEngineMetrics() ?? new TorrentEngineMetrics();

    public TorrentResourceMetrics GetTorrentResourceMetrics(int torrentId)
        => Volatile.Read(ref this.activeEngine)?.GetTorrentResourceMetrics(torrentId);

    public IReadOnlyList<TorrentResourceMetrics> GetAllTorrentResourceMetrics()
        => Volatile.Read(ref this.activeEngine)?.GetAllTorrentResourceMetrics() ?? Array.Empty<TorrentResourceMetrics>();

    public void CheckTrackerHealth()
        => Volatile.Read(ref this.activeEngine)?.CheckTrackerHealth();

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.switchLock.Dispose();
        }
    }
}
