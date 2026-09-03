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

public class QueueManagerService : IQueueManagerService, IHandle<TorrentStatusChangedEvent>, IHandle<TorrentAddedEvent>, IHandle<TorrentDeletedEvent>
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
            var maxDownloads = this.configService.MaxActiveDownloads;
            var maxUploads = this.configService.MaxActiveUploads;
            var maxTotal = this.configService.MaxActiveTorrents;
            var ignoreSlow = this.configService.IgnoreSlowTorrents;
            var slowDownThreshold = this.configService.SlowTorrentDownloadRateThreshold;
            var slowUpThreshold = this.configService.SlowTorrentUploadRateThreshold;

            var allTorrents = this.torrentRepository.All()
                .OrderBy(t => t.QueuePosition > 0 ? t.QueuePosition : t.Id)
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
                    torrent.Status == TorrentStatus.Checking)
                {
                    continue;
                }

                var task = this.downloadEngine?.GetTask(torrent.Id);
                var isComplete = torrent.Progress >= 1.0;

                if (!isComplete)
                {
                    // Downloading candidate
                    var isSlow = ignoreSlow && task != null && task.DownloadSpeed < slowDownThreshold;
                    var canRunDownload = (maxDownloads <= 0 || activeDownloads < maxDownloads || isSlow) &&
                                         (maxTotal <= 0 || activeTotal < maxTotal || isSlow);

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

                        if (!isSlow)
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
                    var isSlow = ignoreSlow && task != null && task.UploadSpeed < slowUpThreshold;
                    var canRunUpload = (maxUploads <= 0 || activeUploads < maxUploads || isSlow) &&
                                       (maxTotal <= 0 || activeTotal < maxTotal || isSlow);

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

                        if (!isSlow)
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
        // Avoid looping when QueueManager updates status to Queued/Downloading/Seeding
        if (message?.NewStatus == TorrentStatus.Paused ||
            message?.NewStatus == TorrentStatus.Stopped ||
            (message?.OldStatus == TorrentStatus.Downloading && message?.NewStatus == TorrentStatus.Seeding))
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
}
