// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
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
    private readonly ITorrentRepository torrentRepository;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim queueLock = new(1, 1);

    public QueueManagerService(
        ITorrentRepository torrentRepository,
        IConfigService configService,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator)
    {
        this.torrentRepository = torrentRepository;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task ProcessQueueAsync()
    {
        if (!await this.queueLock.WaitAsync(0))
        {
            // Already processing queue
            return;
        }

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
            var idleSeedingLimitMinutes = this.configService.IdleSeedingLimitMinutes;

            var allTorrents = this.torrentRepository.All()
                .OrderBy(t => t.QueuePosition > 0 ? t.QueuePosition : int.MaxValue)
                .ThenBy(t => t.Id)
                .ToList();

            if (allTorrents.Count == 0)
            {
                return;
            }

            var activeDownloads = 0;
            var activeUploads = 0;
            var activeTotal = 0;

            foreach (var torrent in allTorrents)
            {
                // Never auto-manage explicitly paused, stopped, or errored torrents
                if (torrent.Status == TorrentStatus.Paused ||
                    torrent.Status == TorrentStatus.Stopped ||
                    torrent.Status == TorrentStatus.Error ||
                    torrent.Status == TorrentStatus.Checking ||
                    torrent.Status == TorrentStatus.Stalled)
                {
                    continue;
                }

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
                        torrent.LastActive = DateTime.UtcNow;
                    }

                    var isSlow = ignoreSlow &&
                                 torrent.Status == TorrentStatus.Downloading &&
                                 task != null &&
                                 task.DownloadSpeed < slowDownThresholdBytes;

                    var isStalled = (task != null && task.IsStalled) ||
                                    (queueStalledEnabled &&
                                     queueStalledMinutes > 0 &&
                                     downloadSpeed == 0 &&
                                     (DateTime.UtcNow - (torrent.LastActive ?? torrent.DateAdded)).TotalMinutes >= queueStalledMinutes);

                    var isIgnoredDownload = isSlow || isStalled;
                    var canRunDownload = (maxDownloads <= 0 || activeDownloads < maxDownloads || isIgnoredDownload) &&
                                         (maxTotal <= 0 || activeTotal < maxTotal || isIgnoredDownload);

                    if (canRunDownload)
                    {
                        if (torrent.Status == TorrentStatus.Queued)
                        {
                            var oldStatus = torrent.Status;
                            torrent.Status = TorrentStatus.Downloading;
                            this.torrentRepository.Update(torrent);

                            try
                            {
                                if (this.downloadEngine != null)
                                {
                                    await this.downloadEngine.ResumeTorrentAsync(torrent.Id);
                                }

                                this.logger.Info("Queue manager promoted torrent {0} ({1}) from Queued to Downloading", torrent.Name, torrent.Id);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to resume torrent {0} in download engine", torrent.Id);
                            }

                            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                            {
                                Torrent = torrent,
                                OldStatus = oldStatus,
                                NewStatus = TorrentStatus.Downloading,
                            });
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
                        if (torrent.Status == TorrentStatus.Downloading)
                        {
                            var oldStatus = torrent.Status;
                            torrent.Status = TorrentStatus.Queued;
                            this.torrentRepository.Update(torrent);

                            try
                            {
                                if (this.downloadEngine != null)
                                {
                                    await this.downloadEngine.PauseTorrentAsync(torrent.Id);
                                }

                                this.logger.Info("Queue manager demoted torrent {0} ({1}) to Queued (Active downloads: {2}/{3})", torrent.Name, torrent.Id, activeDownloads, maxDownloads);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to pause torrent {0} in download engine", torrent.Id);
                            }

                            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                            {
                                Torrent = torrent,
                                OldStatus = oldStatus,
                                NewStatus = TorrentStatus.Queued,
                            });
                        }
                    }
                }
                else
                {
                    // Seeding candidate
                    var uploadSpeed = task != null ? task.UploadSpeed : torrent.UploadSpeed;
                    if (uploadSpeed > 0)
                    {
                        torrent.LastActive = DateTime.UtcNow;
                    }

                    var isSlow = ignoreSlow &&
                                 torrent.Status == TorrentStatus.Seeding &&
                                 task != null &&
                                 task.UploadSpeed < slowUpThresholdBytes;

                    var isIdleSeeder = idleSeedingLimitMinutes > 0 &&
                                       uploadSpeed == 0 &&
                                       (DateTime.UtcNow - (torrent.LastActive ?? torrent.DateCompleted ?? torrent.DateAdded)).TotalMinutes >= idleSeedingLimitMinutes;

                    var isIgnoredUpload = isSlow || isIdleSeeder;
                    var canRunUpload = (maxUploads <= 0 || activeUploads < maxUploads || isIgnoredUpload) &&
                                       (maxTotal <= 0 || activeTotal < maxTotal || isIgnoredUpload);

                    if (canRunUpload)
                    {
                        if (torrent.Status == TorrentStatus.Queued)
                        {
                            var oldStatus = torrent.Status;
                            torrent.Status = TorrentStatus.Seeding;
                            this.torrentRepository.Update(torrent);

                            try
                            {
                                if (this.downloadEngine != null)
                                {
                                    await this.downloadEngine.ResumeTorrentAsync(torrent.Id);
                                }

                                this.logger.Info("Queue manager promoted torrent {0} ({1}) from Queued to Seeding", torrent.Name, torrent.Id);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to resume seeding torrent {0} in download engine", torrent.Id);
                            }

                            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                            {
                                Torrent = torrent,
                                OldStatus = oldStatus,
                                NewStatus = TorrentStatus.Seeding,
                            });
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
                        if (torrent.Status == TorrentStatus.Seeding)
                        {
                            var oldStatus = torrent.Status;
                            torrent.Status = TorrentStatus.Queued;
                            this.torrentRepository.Update(torrent);

                            try
                            {
                                if (this.downloadEngine != null)
                                {
                                    await this.downloadEngine.PauseTorrentAsync(torrent.Id);
                                }

                                this.logger.Info("Queue manager demoted torrent {0} ({1}) to Queued (Active uploads: {2}/{3})", torrent.Name, torrent.Id, activeUploads, maxUploads);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to pause seeding torrent {0} in download engine", torrent.Id);
                            }

                            this.eventAggregator.PublishEvent(new TorrentStatusChangedEvent
                            {
                                Torrent = torrent,
                                OldStatus = oldStatus,
                                NewStatus = TorrentStatus.Queued,
                            });
                        }
                    }
                }
            }
        }
        finally
        {
            this.queueLock.Release();
        }
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message == null)
        {
            return;
        }

        // Trigger queue evaluation when an active torrent vacates a slot (paused, stopped, error, stalled, completed)
        // or when checking finishes and the torrent enters Queued state.
        if (message.NewStatus == TorrentStatus.Paused ||
            message.NewStatus == TorrentStatus.Stopped ||
            message.NewStatus == TorrentStatus.Error ||
            message.NewStatus == TorrentStatus.Stalled ||
            (message.OldStatus == TorrentStatus.Downloading && message.NewStatus == TorrentStatus.Seeding) ||
            (message.OldStatus == TorrentStatus.Checking && message.NewStatus == TorrentStatus.Queued))
        {
            _ = Task.Run(this.ProcessQueueAsync);
        }
    }

    public void Handle(TorrentAddedEvent message)
    {
        _ = Task.Run(this.ProcessQueueAsync);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        _ = Task.Run(this.ProcessQueueAsync);
    }

    public void Handle(ConfigSavedEvent message)
    {
        _ = Task.Run(this.ProcessQueueAsync);
    }
}
