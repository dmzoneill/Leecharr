// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.BitTorrent;

public class DynamicDownloadEngineProxy : IDownloadEngine, ITorrentEngineManager, IHandle<ConfigSavedEvent>, IDisposable
{
    private readonly IEnumerable<ITorrentEngine> availableEngines;
    private readonly IConfigService configService;
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentFileRepository torrentFileRepository;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly SemaphoreSlim switchLock = new(1, 1);
    private ITorrentEngine activeEngine;
    private ITorrentEngine migratingTargetEngine;
    private TaskCompletionSource migrationTcs;
    private bool disposed;

    public string ProtocolName => this.GetActiveOrMigratingEngine()?.ProtocolName ?? "BitTorrent";

    public ITorrentEngine ActiveEngine => Volatile.Read(ref this.activeEngine);

    public string ActiveEngineId => Volatile.Read(ref this.activeEngine)?.EngineId ?? "MonoTorrent";

    public int DhtNodeCount => this.GetActiveOrMigratingEngine()?.DhtNodeCount ?? 0;

    public DynamicDownloadEngineProxy(
        IEnumerable<ITorrentEngine> availableEngines,
        IConfigService configService,
        ITorrentRepository torrentRepository,
        IEventAggregator eventAggregator,
        ITorrentFileRepository torrentFileRepository = null,
        IAppFolderInfo appFolderInfo = null,
        ITrackerEntryRepository trackerEntryRepository = null)
    {
        this.availableEngines = availableEngines;
        this.configService = configService;
        this.torrentRepository = torrentRepository;
        this.torrentFileRepository = torrentFileRepository;
        this.appFolderInfo = appFolderInfo;
        this.trackerEntryRepository = trackerEntryRepository;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredEngineId = this.configService.ActiveTorrentEngine;
        this.activeEngine = this.availableEngines.FirstOrDefault(e => e.IsAvailable && e.EngineId.Equals(desiredEngineId, StringComparison.OrdinalIgnoreCase))
                        ?? this.availableEngines.FirstOrDefault(e => e.IsAvailable && e.EngineId.Equals("MonoTorrent", StringComparison.OrdinalIgnoreCase))
                        ?? this.availableEngines.FirstOrDefault(e => e.IsAvailable)
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

        if (!targetEngine.IsAvailable)
        {
            return new EngineSwitchResult
            {
                Success = false,
                PreviousEngine = Volatile.Read(ref this.activeEngine).EngineId,
                ActiveEngine = Volatile.Read(ref this.activeEngine).EngineId,
                Error = $"Cannot switch to engine '{targetEngine.DisplayName}': engine is not available.",
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
        var previousEngine = Volatile.Read(ref this.activeEngine);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            EngineHealthCheckResult health;
            try
            {
                health = await targetEngine.ProbeHealthAsync().WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return new EngineSwitchResult
                {
                    Success = false,
                    PreviousEngine = previousEngine.EngineId,
                    ActiveEngine = previousEngine.EngineId,
                    Error = $"Cannot switch to engine '{targetEngine.DisplayName}': health check timed out after 5 seconds.",
                };
            }

            if (!health.IsHealthy)
            {
                return new EngineSwitchResult
                {
                    Success = false,
                    PreviousEngine = previousEngine.EngineId,
                    ActiveEngine = previousEngine.EngineId,
                    Error = $"Cannot switch to engine '{targetEngine.DisplayName}': health check failed ({health.StatusMessage}).",
                };
            }

            this.logger.Info("Initiating zero-downtime hot-swap: {0} -> {1} (PreserveTransfers: {2})", previousEngine.EngineId, targetEngine.EngineId, preserveTransfers);
            var rehydrated = 0;

            // Set up migration gating
            this.migrationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref this.migratingTargetEngine, targetEngine);

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

            // Hot-swap active pointer to target engine and release migration queue
            Volatile.Write(ref this.activeEngine, targetEngine);
            this.migrationTcs.TrySetResult();

            // 3. Migrate active torrents if requested
            if (preserveTransfers)
            {
                var allTorrents = this.torrentRepository.All();
                foreach (var torrent in allTorrents)
                {
                    try
                    {
                        byte[] torrentBytes = null;
                        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
                        {
                            var hash = torrent.InfoHash.ToLowerInvariant();
                            var pathsToTry = new List<string>();

                            if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
                            {
                                pathsToTry.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Torrents", $"{hash}.torrent"));
                            }

                            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                            if (!string.IsNullOrWhiteSpace(appData))
                            {
                                pathsToTry.Add(Path.Combine(appData, "Torrents", $"{hash}.torrent"));
                                pathsToTry.Add(Path.Combine(appData, "Leecharr", "Torrents", $"{hash}.torrent"));
                            }

                            foreach (var path in pathsToTry)
                            {
                                if (File.Exists(path))
                                {
                                    try
                                    {
                                        torrentBytes = await File.ReadAllBytesAsync(path);
                                        if (torrentBytes != null && torrentBytes.Length > 0)
                                        {
                                            break;
                                        }
                                    }
                                    catch
                                    {
                                        // Fallback to next path or magnet
                                    }
                                }
                            }
                        }

                        var magnetUri = !string.IsNullOrWhiteSpace(torrent.TrackerUrl)
                            ? $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}"
                            : $"magnet:?xt=urn:btih:{torrent.InfoHash}";

                        await targetEngine.AddTorrentAsync(torrent, torrentBytes, magnetUri);

                        if (this.torrentFileRepository != null)
                        {
                            var files = this.torrentFileRepository.GetByTorrentId(torrent.Id);
                            foreach (var file in files)
                            {
                                if (file.Priority != 3)
                                {
                                    await targetEngine.SetFilePriorityAsync(torrent.Id, file.Path, file.Priority);
                                }
                            }
                        }

                        if (torrent.DownloadLimit > 0 || torrent.UploadLimit > 0)
                        {
                            await targetEngine.SetTorrentRateLimitsAsync(torrent.Id, torrent.DownloadLimit, torrent.UploadLimit);
                        }

                        if (torrent.InitialSeeding)
                        {
                            await targetEngine.SetSuperSeedingAsync(torrent.Id, true);
                        }

                        if (torrent.IsPrivate)
                        {
                            await targetEngine.SetTorrentPrivateStatusAsync(torrent.Id, true);
                        }

                        if (torrentBytes == null && this.trackerEntryRepository != null)
                        {
                            var extraTrackers = this.trackerEntryRepository.GetByTorrentId(torrent.Id)
                                .Select(t => t.Url)
                                .Where(u => !string.IsNullOrWhiteSpace(u) && !string.Equals(u, torrent.TrackerUrl, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (extraTrackers.Count > 0)
                            {
                                await targetEngine.AddTrackersAsync(torrent.Id, extraTrackers);
                            }
                        }

                        if (torrent.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Queued or TorrentStatus.Error or TorrentStatus.Stalled)
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

            // 4. Persist setting to configuration
            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveTorrentEngine", targetEngine.EngineId },
            });

            this.logger.Info("Engine hot-swap completed: {0} -> {1} ({2} torrents migrated)", previousEngine.EngineId, targetEngine.EngineId, rehydrated);

            // 5. Broadcast event
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
            this.logger.Error(ex, "Fatal error during engine hot-swap to {0}. Attempting rollback to {1}...", targetEngineId, previousEngine?.EngineId);

            if (targetEngine != null)
            {
                try
                {
                    await targetEngine.StopAsync();
                }
                catch
                {
                    // ignore
                }
            }

            if (previousEngine != null)
            {
                try
                {
                    await previousEngine.StartAsync();
                }
                catch (Exception rollbackEx)
                {
                    this.logger.Error(rollbackEx, "Failed to rollback and restart previous engine {0}", previousEngine.EngineId);
                }

                Volatile.Write(ref this.activeEngine, previousEngine);
            }

            return new EngineSwitchResult
            {
                Success = false,
                PreviousEngine = previousEngine?.EngineId,
                ActiveEngine = previousEngine?.EngineId,
                Error = $"Hot-swap failed: {ex.Message}",
            };
        }
        finally
        {
            Volatile.Write(ref this.migratingTargetEngine, null);
            this.migrationTcs?.TrySetResult();
            this.switchLock.Release();
        }
    }

    public bool IsHaltedByKillSwitch => this.GetActiveOrMigratingEngine()?.IsHaltedByKillSwitch ?? false;

    public async Task StartAsync()
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.StartAsync();
        }
    }

    public async Task StopAsync()
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.StopAsync();
        }
    }

    public async Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
    {
        var engine = await this.GetReadyEngineAsync();
        return await engine.AddTorrentAsync(torrent, torrentFileBytes, magnetUri);
    }

    public async Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.RemoveTorrentAsync(torrentId, deleteFiles);
    }

    public async Task PauseTorrentAsync(int torrentId)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.PauseTorrentAsync(torrentId);
    }

    public async Task PauseAllTorrentsAsync()
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.PauseAllTorrentsAsync();
        }
    }

    public async Task PauseAllAsync()
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.PauseAllAsync();
        }
    }

    public async Task ResumeTorrentAsync(int torrentId)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.ResumeTorrentAsync(torrentId);
    }

    public async Task ResumeAllTorrentsAsync()
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.ResumeAllTorrentsAsync();
        }
    }

    public async Task ResumeAllAsync()
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.ResumeAllAsync();
        }
    }

    public async Task ForceRecheckAsync(int torrentId)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.ForceRecheckAsync(torrentId);
    }

    public async Task ForceAnnounceAsync(int torrentId)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.ForceAnnounceAsync(torrentId);
    }

    public async Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.AddTrackersAsync(torrentId, trackers);
    }

    public async Task RemoveTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.RemoveTrackersAsync(torrentId, trackers);
    }

    public async Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.SetFilePriorityAsync(torrentId, filePath, priority);
    }

    public async Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.SetRateLimitsAsync(maxDownloadKbps, maxUploadKbps);
    }

    public async Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps)
    {
        var engine = await this.GetReadyEngineAsync();
        await engine.SetTorrentRateLimitsAsync(torrentId, maxDownloadKbps, maxUploadKbps);
    }

    public async Task SetTorrentPrivateStatusAsync(int torrentId, bool isPrivate)
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.SetTorrentPrivateStatusAsync(torrentId, isPrivate);
        }
    }

    public async Task SetSuperSeedingAsync(int torrentId, bool enabled)
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.SetSuperSeedingAsync(torrentId, enabled);
        }
    }

    public async Task<bool> RenameFileAsync(int torrentId, string oldRelativePath, string newRelativePath)
    {
        var engine = await this.GetReadyEngineAsync();
        return engine != null && await engine.RenameFileAsync(torrentId, oldRelativePath, newRelativePath);
    }

    public async Task<bool> RenameFolderAsync(int torrentId, string oldRelativeFolder, string newRelativeFolder)
    {
        var engine = await this.GetReadyEngineAsync();
        return engine != null && await engine.RenameFolderAsync(torrentId, oldRelativeFolder, newRelativeFolder);
    }

    public async Task MoveTorrentFilesAsync(int torrentId, string newSavePath, bool moveFiles = true)
    {
        var engine = await this.GetReadyEngineAsync();
        if (engine != null)
        {
            await engine.MoveTorrentFilesAsync(torrentId, newSavePath, moveFiles);
        }
    }

    public async Task<EngineHealthCheckResult> ProbeHealthAsync()
    {
        var engine = await this.GetReadyEngineAsync();
        return engine != null ? await engine.ProbeHealthAsync() : new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" };
    }

    public IDownloadTask GetTask(int torrentId)
        => this.GetActiveOrMigratingEngine()?.GetTask(torrentId);

    public IEnumerable<IDownloadTask> GetAllTasks()
        => this.GetActiveOrMigratingEngine()?.GetAllTasks() ?? Enumerable.Empty<IDownloadTask>();

    public TorrentEngineMetrics GetEngineMetrics()
        => this.GetActiveOrMigratingEngine()?.GetEngineMetrics() ?? new TorrentEngineMetrics();

    public TorrentResourceMetrics GetTorrentResourceMetrics(int torrentId)
        => this.GetActiveOrMigratingEngine()?.GetTorrentResourceMetrics(torrentId);

    public IReadOnlyList<TorrentResourceMetrics> GetAllTorrentResourceMetrics()
        => this.GetActiveOrMigratingEngine()?.GetAllTorrentResourceMetrics() ?? Array.Empty<TorrentResourceMetrics>();

    public void CheckTrackerHealth()
        => this.GetActiveOrMigratingEngine()?.CheckTrackerHealth();

    public void CheckDiskSpaceHealth()
        => this.GetActiveOrMigratingEngine()?.CheckDiskSpaceHealth();

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.switchLock.Dispose();
        }
    }

    public void Handle(ConfigSavedEvent message)
    {
        var desiredEngineId = this.configService?.ActiveTorrentEngine;
        if (!string.IsNullOrWhiteSpace(desiredEngineId) &&
            !string.Equals(this.ActiveEngineId, desiredEngineId, StringComparison.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.SwitchEngineAsync(desiredEngineId);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Failed to switch active torrent engine on ConfigSavedEvent to {0}", desiredEngineId);
                }
            });
        }
    }

    private ITorrentEngine GetActiveOrMigratingEngine()
    {
        return Volatile.Read(ref this.migratingTargetEngine) ?? Volatile.Read(ref this.activeEngine);
    }

    private async Task<ITorrentEngine> GetReadyEngineAsync()
    {
        var tcs = Volatile.Read(ref this.migrationTcs);
        if (tcs != null && !tcs.Task.IsCompleted)
        {
            await tcs.Task;
        }

        return Volatile.Read(ref this.activeEngine);
    }
}
