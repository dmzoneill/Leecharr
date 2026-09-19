// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class QueueManagerService : IQueueManagerService, IHandle<TorrentStatusChangedEvent>, IHandle<TorrentAddedEvent>, IHandle<TorrentDeletedEvent>, IHandle<ConfigSavedEvent>
{
    public const int DefaultRequiredSlowTicks = 3;
    public static readonly TimeSpan DefaultMinimumActiveCooldown = TimeSpan.FromSeconds(30);

    private readonly ITorrentRepository torrentRepository;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim queueLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TorrentQueueState> torrentStates = new();
    private readonly int requiredSlowTicks;
    private readonly TimeSpan minimumActiveCooldown;
    private int pendingRuns;

    public QueueManagerService(
        ITorrentRepository torrentRepository,
        IConfigService configService,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator)
        : this(torrentRepository, configService, downloadEngine, eventAggregator, DefaultRequiredSlowTicks, DefaultMinimumActiveCooldown)
    {
    }

    internal QueueManagerService(
        ITorrentRepository torrentRepository,
        IConfigService configService,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator,
        int requiredSlowTicks,
        TimeSpan? minimumActiveCooldown = null)
    {
        this.torrentRepository = torrentRepository;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
        this.requiredSlowTicks = requiredSlowTicks > 0 ? requiredSlowTicks : DefaultRequiredSlowTicks;
        this.minimumActiveCooldown = minimumActiveCooldown ?? DefaultMinimumActiveCooldown;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task ProcessQueueAsync()
    {
        Interlocked.Increment(ref this.pendingRuns);
        while (true)
        {
            if (!await this.queueLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                while (Interlocked.Exchange(ref this.pendingRuns, 0) > 0)
                {
                    await this.ProcessQueueInternalAsync();
                }
            }
            finally
            {
                this.queueLock.Release();
            }

            if (Volatile.Read(ref this.pendingRuns) == 0)
            {
                break;
            }
        }
    }

    private async Task ProcessQueueInternalAsync()
    {
        var eventsToPublish = new List<TorrentStatusChangedEvent>();

        try
        {
            var maxDownloads = this.configService.DownloadQueueSize > 0
                ? this.configService.DownloadQueueSize
                : this.configService.MaxActiveDownloads;
            var maxUploads = this.configService.SeedQueueSize > 0
                ? this.configService.SeedQueueSize
                : this.configService.MaxActiveUploads;
            var maxTotal = this.configService.MaxActiveTorrents;
            var ignoreSlow = this.configService.IgnoreSlowTorrents;
            var slowDownThreshold = this.configService.SlowTorrentDownloadRateThreshold;
            var slowUpThreshold = this.configService.SlowTorrentUploadRateThreshold;
            var slowDownThresholdBytes = slowDownThreshold * 1024L;
            var slowUpThresholdBytes = slowUpThreshold * 1024L;
            var queueStalledEnabled = this.configService.QueueStalledEnabled;
            var queueStalledMinutes = this.configService.QueueStalledMinutes;
            var magnetMetadataTimeoutSeconds = this.configService.MagnetMetadataTimeoutSeconds > 0
                ? this.configService.MagnetMetadataTimeoutSeconds
                : 180;
            var idleSeedingLimitMinutes = this.configService.IdleSeedingLimitMinutes;

            var allTorrents = this.torrentRepository.All()
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.QueuePosition > 0 ? t.QueuePosition : int.MaxValue)
                .ThenBy(t => t.Id)
                .ToList();

            if (allTorrents.Count == 0)
            {
                this.torrentStates.Clear();
                return;
            }

            var torrentIds = new HashSet<int>(allTorrents.Select(t => t.Id));
            foreach (var id in this.torrentStates.Keys)
            {
                if (!torrentIds.Contains(id))
                {
                    this.torrentStates.TryRemove(id, out _);
                }
            }

            var activeDownloads = 0;
            var activeUploads = 0;
            var activeTotal = 0;

            foreach (var torrent in allTorrents)
            {
                // Never auto-manage explicitly paused, stopped, completed, or errored torrents
                if (torrent.Status == TorrentStatus.Paused ||
                    torrent.Status == TorrentStatus.Stopped ||
                    torrent.Status == TorrentStatus.Completed ||
                    torrent.Status == TorrentStatus.Error ||
                    torrent.Status == TorrentStatus.Checking ||
                    torrent.Status == TorrentStatus.Stalled)
                {
                    continue;
                }

                // Exclude torrents with Priority < 0 (e.g. DoNotDownload) from being promoted
                if (torrent.Priority < 0 && torrent.Status == TorrentStatus.Queued)
                {
                    continue;
                }

                var oldStatus = torrent.Status;
                var previousLastActive = torrent.LastActive;
                var shouldPersistLastActive = false;

                var state = this.torrentStates.GetOrAdd(torrent.Id, _ => new TorrentQueueState());
                var task = this.downloadEngine?.GetTask(torrent.Id);
                var isComplete = torrent.Status == TorrentStatus.Seeding ||
                                 torrent.Progress >= 1.0 ||
                                 torrent.DateCompleted.HasValue;

                if (!isComplete)
                {
                    // Downloading candidate
                    var downloadSpeed = task != null ? task.DownloadSpeed : torrent.DownloadSpeed;
                    if (downloadSpeed > 0)
                    {
                        var now = DateTime.UtcNow;
                        torrent.LastActive = now;
                        if (!previousLastActive.HasValue || (now - previousLastActive.Value).TotalSeconds >= 30)
                        {
                            shouldPersistLastActive = true;
                        }
                    }

                    var isCurrentlySlow = ignoreSlow &&
                                         torrent.Status == TorrentStatus.Downloading &&
                                         task != null &&
                                         task.DownloadSpeed < slowDownThresholdBytes;

                    if (isCurrentlySlow)
                    {
                        state.SlowDownloadTicks++;
                    }
                    else
                    {
                        state.SlowDownloadTicks = 0;
                    }

                    var isSlow = isCurrentlySlow && state.SlowDownloadTicks >= this.requiredSlowTicks;

                    var isStalled = ((task != null && task.IsStalled) ||
                                    (queueStalledEnabled &&
                                     queueStalledMinutes > 0 &&
                                     downloadSpeed == 0 &&
                                     (DateTime.UtcNow - (torrent.LastActive ?? torrent.DateAdded)).TotalMinutes >= queueStalledMinutes)) &&
                                    torrent.Status == TorrentStatus.Downloading;

                    var isResolvingMetadata = (torrent.TotalSize == 0 || torrent.PieceCount == 0) &&
                                              torrent.Progress <= 0 &&
                                              (task == null || (task.TotalSize == 0 && task.Progress <= 0));

                    var isMagnetTimeout = false;
                    if (isResolvingMetadata && downloadSpeed == 0)
                    {
                        var resolutionStartTime = torrent.LastActive ?? (torrent.DateAdded != default ? torrent.DateAdded : (state.MetadataStartedAt ??= DateTime.UtcNow));
                        var elapsedResolution = DateTime.UtcNow - resolutionStartTime;

                        if ((magnetMetadataTimeoutSeconds > 0 && elapsedResolution.TotalSeconds >= magnetMetadataTimeoutSeconds) ||
                            (queueStalledEnabled && queueStalledMinutes > 0 && elapsedResolution.TotalMinutes >= queueStalledMinutes))
                        {
                            isMagnetTimeout = true;
                        }
                    }
                    else if (!isResolvingMetadata)
                    {
                        state.MetadataStartedAt = null;
                    }

                    var isIgnoredDownload = isSlow || isStalled || torrent.ForceStart;
                    var canRunDownload = torrent.ForceStart ||
                                         (!isMagnetTimeout &&
                                          ((maxDownloads <= 0 || activeDownloads < maxDownloads || isIgnoredDownload) &&
                                           (maxTotal <= 0 || activeTotal < maxTotal || isIgnoredDownload)));

                    if (canRunDownload)
                    {
                        if (torrent.Status == TorrentStatus.Queued)
                        {
                            oldStatus = torrent.Status;
                            try
                            {
                                if (this.downloadEngine != null)
                                {
                                    await this.downloadEngine.ResumeTorrentAsync(torrent.Id);
                                }

                                torrent.Status = TorrentStatus.Downloading;
                                state.ActivatedAt = DateTime.UtcNow;
                                state.SlowDownloadTicks = 0;
                                this.torrentRepository.Update(torrent);
                                this.logger.Info("[State Machine] Queue manager promoted torrent #{0} ('{1}') from Queued to Downloading", torrent.Id, torrent.Name);

                                eventsToPublish.Add(new TorrentStatusChangedEvent
                                {
                                    Torrent = torrent,
                                    OldStatus = oldStatus,
                                    NewStatus = TorrentStatus.Downloading,
                                    IsQueueManagerInternal = true,
                                });
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to resume torrent {0} in download engine", torrent.Id);
                            }
                        }

                        if (!isIgnoredDownload)
                        {
                            activeDownloads++;
                            activeTotal++;
                        }
                    }
                    else
                    {
                        // Exceeded download concurrency limit
                        if (torrent.Status == TorrentStatus.Downloading && !torrent.ForceStart)
                        {
                            var inCooldown = !isMagnetTimeout &&
                                             state.ActivatedAt.HasValue &&
                                             (DateTime.UtcNow - state.ActivatedAt.Value) < this.minimumActiveCooldown;

                            if (inCooldown)
                            {
                                this.logger.Debug("[State Machine] Torrent #{0} ('{1}') is in active run cooldown window; deferring demotion", torrent.Id, torrent.Name);
                                if (!isIgnoredDownload)
                                {
                                    activeDownloads++;
                                    activeTotal++;
                                }
                            }
                            else
                            {
                                oldStatus = torrent.Status;
                                try
                                {
                                    if (this.downloadEngine != null)
                                    {
                                        await this.downloadEngine.PauseTorrentAsync(torrent.Id);
                                    }

                                    torrent.Status = TorrentStatus.Queued;
                                    torrent.DownloadSpeed = 0;
                                    torrent.UploadSpeed = 0;
                                    torrent.Eta = 0;
                                    torrent.Seeders = 0;
                                    torrent.Leechers = 0;
                                    state.ActivatedAt = null;
                                    state.SlowDownloadTicks = 0;
                                    this.torrentRepository.Update(torrent);
                                    this.logger.Info("[State Machine] Queue manager demoted torrent #{0} ('{1}') from Downloading to Queued (Active downloads: {2}/{3})", torrent.Id, torrent.Name, activeDownloads, maxDownloads);

                                    eventsToPublish.Add(new TorrentStatusChangedEvent
                                    {
                                        Torrent = torrent,
                                        OldStatus = oldStatus,
                                        NewStatus = TorrentStatus.Queued,
                                        IsQueueManagerInternal = true,
                                    });
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Warn(ex, "Failed to pause torrent {0} in download engine", torrent.Id);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Seeding candidate
                    var uploadSpeed = task != null ? task.UploadSpeed : torrent.UploadSpeed;
                    if (uploadSpeed > 0)
                    {
                        var now = DateTime.UtcNow;
                        torrent.LastActive = now;
                        if (!previousLastActive.HasValue || (now - previousLastActive.Value).TotalSeconds >= 30)
                        {
                            shouldPersistLastActive = true;
                        }
                    }

                    var isCurrentlySlow = ignoreSlow &&
                                         torrent.Status == TorrentStatus.Seeding &&
                                         task != null &&
                                         task.UploadSpeed < slowUpThresholdBytes;

                    if (isCurrentlySlow)
                    {
                        state.SlowUploadTicks++;
                    }
                    else
                    {
                        state.SlowUploadTicks = 0;
                    }

                    var isSlow = isCurrentlySlow && state.SlowUploadTicks >= this.requiredSlowTicks;

                    var isIdleSeeder = idleSeedingLimitMinutes > 0 &&
                                       torrent.Status == TorrentStatus.Seeding &&
                                       uploadSpeed == 0 &&
                                       (DateTime.UtcNow - (torrent.LastActive ?? torrent.DateCompleted ?? torrent.DateAdded)).TotalMinutes >= idleSeedingLimitMinutes;

                    var isIgnoredUpload = isSlow || isIdleSeeder || torrent.ForceStart;
                    var canRunUpload = torrent.ForceStart ||
                                       ((maxUploads <= 0 || activeUploads < maxUploads || isIgnoredUpload) &&
                                        (maxTotal <= 0 || activeTotal < maxTotal || isIgnoredUpload));

                    if (canRunUpload)
                    {
                        if (torrent.Status == TorrentStatus.Queued)
                        {
                            oldStatus = torrent.Status;
                            try
                            {
                                if (this.downloadEngine != null)
                                {
                                    await this.downloadEngine.ResumeTorrentAsync(torrent.Id);
                                }

                                torrent.Status = TorrentStatus.Seeding;
                                state.ActivatedAt = DateTime.UtcNow;
                                state.SlowUploadTicks = 0;
                                this.torrentRepository.Update(torrent);
                                this.logger.Info("[State Machine] Queue manager promoted torrent #{0} ('{1}') from Queued to Seeding", torrent.Id, torrent.Name);

                                eventsToPublish.Add(new TorrentStatusChangedEvent
                                {
                                    Torrent = torrent,
                                    OldStatus = oldStatus,
                                    NewStatus = TorrentStatus.Seeding,
                                    IsQueueManagerInternal = true,
                                });
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to resume seeding torrent {0} in download engine", torrent.Id);
                            }
                        }

                        if (!isIgnoredUpload)
                        {
                            activeUploads++;
                            activeTotal++;
                        }
                    }
                    else
                    {
                        // Exceeded upload concurrency limit
                        if (torrent.Status == TorrentStatus.Seeding && !torrent.ForceStart)
                        {
                            var inCooldown = state.ActivatedAt.HasValue &&
                                             (DateTime.UtcNow - state.ActivatedAt.Value) < this.minimumActiveCooldown;

                            if (inCooldown)
                            {
                                this.logger.Debug("[State Machine] Seeding torrent #{0} ('{1}') is in active run cooldown window; deferring demotion", torrent.Id, torrent.Name);
                                if (!isIgnoredUpload)
                                {
                                    activeUploads++;
                                    activeTotal++;
                                }
                            }
                            else
                            {
                                oldStatus = torrent.Status;
                                try
                                {
                                    if (this.downloadEngine != null)
                                    {
                                        await this.downloadEngine.PauseTorrentAsync(torrent.Id);
                                    }

                                    torrent.Status = TorrentStatus.Queued;
                                    torrent.DownloadSpeed = 0;
                                    torrent.UploadSpeed = 0;
                                    torrent.Eta = 0;
                                    torrent.Seeders = 0;
                                    torrent.Leechers = 0;
                                    state.ActivatedAt = null;
                                    state.SlowUploadTicks = 0;
                                    this.torrentRepository.Update(torrent);
                                    this.logger.Info("[State Machine] Queue manager demoted torrent #{0} ('{1}') from Seeding to Queued (Active uploads: {2}/{3})", torrent.Id, torrent.Name, activeUploads, maxUploads);

                                    eventsToPublish.Add(new TorrentStatusChangedEvent
                                    {
                                        Torrent = torrent,
                                        OldStatus = oldStatus,
                                        NewStatus = TorrentStatus.Queued,
                                        IsQueueManagerInternal = true,
                                    });
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Warn(ex, "Failed to pause seeding torrent {0} in download engine", torrent.Id);
                                }
                            }
                        }
                    }
                }

                if (shouldPersistLastActive && torrent.Status == oldStatus)
                {
                    this.torrentRepository.Update(torrent);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to process queue");
        }

        foreach (var evt in eventsToPublish)
        {
            this.eventAggregator.PublishEvent(evt);
        }
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message == null || message.IsQueueManagerInternal || message.OldStatus == message.NewStatus)
        {
            return;
        }

        if (message.Torrent != null)
        {
            if (message.NewStatus == TorrentStatus.Paused ||
                message.NewStatus == TorrentStatus.Stopped ||
                message.NewStatus == TorrentStatus.Completed ||
                message.NewStatus == TorrentStatus.Error ||
                message.NewStatus == TorrentStatus.Queued)
            {
                if (this.torrentStates.TryGetValue(message.Torrent.Id, out var state))
                {
                    state.ActivatedAt = null;
                    state.SlowDownloadTicks = 0;
                    state.SlowUploadTicks = 0;
                }
            }
        }

        _ = Task.Run(this.ProcessQueueAsync);
    }

    public void Handle(TorrentAddedEvent message)
    {
        _ = Task.Run(this.ProcessQueueAsync);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent != null)
        {
            this.torrentStates.TryRemove(message.Torrent.Id, out _);
        }

        _ = Task.Run(this.ProcessQueueAsync);
    }

    public void Handle(ConfigSavedEvent message)
    {
        _ = Task.Run(this.ProcessQueueAsync);
    }

    private sealed class TorrentQueueState
    {
        public int SlowDownloadTicks { get; set; }

        public int SlowUploadTicks { get; set; }

        public DateTime? ActivatedAt { get; set; }

        public DateTime? MetadataStartedAt { get; set; }
    }
}
