// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Bandwidth.Distribution;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Bandwidth;

public interface IBandwidthDistributionService
{
    Task ApplyPerTorrentLimitsAsync(EffectiveSpeedLimits globalLimits);
}

public class BandwidthDistributionService : IBandwidthDistributionService, IHandle<ConfigSavedEvent>
{
    private readonly IDownloadEngine downloadEngine;
    private readonly ITorrentRepository torrentRepository;
    private readonly ICategoryService categoryService;
    private readonly ISpeedDistributionManager distributionManager;
    private readonly IBandwidthStopSelection stopSelection;
    private readonly IConfigService configService;
    private readonly Func<double> randomRollProvider;
    private readonly Logger logger;

    public BandwidthDistributionService(
        IDownloadEngine downloadEngine,
        ITorrentRepository torrentRepository,
        ICategoryService categoryService,
        ISpeedDistributionManager distributionManager,
        IBandwidthStopSelection stopSelection,
        IConfigService configService,
        Func<double> randomRollProvider = null)
    {
        this.downloadEngine = downloadEngine;
        this.torrentRepository = torrentRepository;
        this.categoryService = categoryService;
        this.distributionManager = distributionManager;
        this.stopSelection = stopSelection;
        this.configService = configService;
        this.randomRollProvider = randomRollProvider ?? (() => Random.Shared.NextDouble());
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ConfigSavedEvent message)
    {
        this.distributionManager.InvalidateCache();
    }

    public async Task ApplyPerTorrentLimitsAsync(EffectiveSpeedLimits globalLimits)
    {
        if (this.downloadEngine == null || globalLimits == null)
        {
            return;
        }

        if (globalLimits.IsDownloadPaused && globalLimits.IsUploadPaused)
        {
            return;
        }

        var tasks = this.downloadEngine.GetAllTasks()?.ToList();
        if (tasks == null || tasks.Count == 0)
        {
            return;
        }

        var torrentsById = this.torrentRepository?.All()?.ToDictionary(t => t.Id) ?? new Dictionary<int, Torrent>();
        var limitsByTorrentId = new Dictionary<int, (int DownloadKbps, int UploadKbps)>();

        foreach (var task in tasks)
        {
            if (!torrentsById.TryGetValue(task.TorrentId, out var torrent))
            {
                continue;
            }

            limitsByTorrentId[task.TorrentId] = (
                this.ResolveTorrentDownloadLimitKbps(torrent),
                this.ResolveTorrentUploadLimitKbps(torrent));
        }

        if (!globalLimits.IsUploadPaused && globalLimits.MaxUploadSpeedKbps > 0)
        {
            this.ApplyUploadDistribution(globalLimits.MaxUploadSpeedKbps, tasks, torrentsById, limitsByTorrentId);
        }

        if (!globalLimits.IsDownloadPaused && globalLimits.MaxDownloadSpeedKbps > 0)
        {
            this.ApplyDownloadDistribution(globalLimits.MaxDownloadSpeedKbps, tasks, torrentsById, limitsByTorrentId);
        }

        foreach (var (torrentId, limits) in limitsByTorrentId)
        {
            try
            {
                await this.downloadEngine.SetTorrentRateLimitsAsync(torrentId, limits.DownloadKbps, limits.UploadKbps);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to apply distributed rate limits for torrent {0}", torrentId);
            }
        }
    }

    private void ApplyUploadDistribution(
        int globalUploadKbps,
        List<IDownloadTask> tasks,
        Dictionary<int, Torrent> torrentsById,
        Dictionary<int, (int DownloadKbps, int UploadKbps)> limitsByTorrentId)
    {
        var uploadCandidates = new List<(Torrent Torrent, IDownloadTask Task)>();
        foreach (var task in tasks)
        {
            if (task.Status != TorrentStatus.Seeding)
            {
                continue;
            }

            if (!torrentsById.TryGetValue(task.TorrentId, out var torrent))
            {
                continue;
            }

            uploadCandidates.Add((torrent, task));
        }

        if (uploadCandidates.Count == 0)
        {
            return;
        }

        var torrentList = uploadCandidates.Select(c => c.Torrent).ToList();
        var stopped = this.stopSelection.SelectUploadStoppedIndices(torrentList);
        var maxUploadBps = (long)globalUploadKbps * 1024L;
        var weights = torrentList.Select(t => GetPriorityWeight(t.Priority)).ToArray();
        var caps = torrentList.Select(t =>
        {
            var capKbps = this.ResolveTorrentUploadLimitKbps(t);
            return capKbps > 0 ? (long)capKbps * 1024L : long.MaxValue;
        }).ToArray();

        var speeds = this.distributionManager.DistributeUploadSpeeds(torrentList.Count, maxUploadBps, weights, caps)
            ?? Array.Empty<long>();

        for (var i = 0; i < uploadCandidates.Count; i++)
        {
            var torrentId = uploadCandidates[i].Torrent.Id;
            if (!limitsByTorrentId.TryGetValue(torrentId, out var current))
            {
                continue;
            }

            var uploadKbps = 0;
            if (!stopped.Contains(i))
            {
                var bytesPerSecond = i < speeds.Length ? speeds[i] : 0L;
                bytesPerSecond = this.ApplySpeedVariation(bytesPerSecond);
                uploadKbps = BytesPerSecondToKbps(bytesPerSecond);
                if (current.UploadKbps > 0)
                {
                    uploadKbps = Math.Min(uploadKbps, current.UploadKbps);
                }
            }
            else
            {
                uploadKbps = 1;
            }

            limitsByTorrentId[torrentId] = (current.DownloadKbps, uploadKbps);
        }
    }

    private void ApplyDownloadDistribution(
        int globalDownloadKbps,
        List<IDownloadTask> tasks,
        Dictionary<int, Torrent> torrentsById,
        Dictionary<int, (int DownloadKbps, int UploadKbps)> limitsByTorrentId)
    {
        var downloadCandidates = new List<(Torrent Torrent, IDownloadTask Task)>();
        foreach (var task in tasks)
        {
            if (task.Status != TorrentStatus.Downloading)
            {
                continue;
            }

            if (!torrentsById.TryGetValue(task.TorrentId, out var torrent))
            {
                continue;
            }

            downloadCandidates.Add((torrent, task));
        }

        if (downloadCandidates.Count == 0)
        {
            return;
        }

        var torrentList = downloadCandidates.Select(c => c.Torrent).ToList();
        var stopped = this.stopSelection.SelectDownloadStoppedIndices(torrentList);
        var maxDownloadBps = (long)globalDownloadKbps * 1024L;
        var weights = torrentList.Select(t => GetPriorityWeight(t.Priority)).ToArray();
        var caps = torrentList.Select(t =>
        {
            var capKbps = this.ResolveTorrentDownloadLimitKbps(t);
            return capKbps > 0 ? (long)capKbps * 1024L : long.MaxValue;
        }).ToArray();

        var speeds = this.distributionManager.DistributeDownloadSpeeds(torrentList.Count, maxDownloadBps, weights, caps)
            ?? Array.Empty<long>();

        for (var i = 0; i < downloadCandidates.Count; i++)
        {
            var torrentId = downloadCandidates[i].Torrent.Id;
            if (!limitsByTorrentId.TryGetValue(torrentId, out var current))
            {
                continue;
            }

            var downloadKbps = 0;
            if (!stopped.Contains(i))
            {
                var bytesPerSecond = i < speeds.Length ? speeds[i] : 0L;
                bytesPerSecond = this.ApplySpeedVariation(bytesPerSecond);
                downloadKbps = BytesPerSecondToKbps(bytesPerSecond);
                if (current.DownloadKbps > 0)
                {
                    downloadKbps = Math.Min(downloadKbps, current.DownloadKbps);
                }
            }
            else
            {
                downloadKbps = 1;
            }

            limitsByTorrentId[torrentId] = (downloadKbps, current.UploadKbps);
        }
    }

    private long ApplySpeedVariation(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return 0;
        }

        var min = this.configService.SpeedVariationMin;
        var max = this.configService.SpeedVariationMax;
        if (max < min)
        {
            (min, max) = (max, min);
        }

        var factor = min + (this.randomRollProvider() * (max - min));
        return (long)Math.Max(1, Math.Round(bytesPerSecond * factor));
    }

    private static int BytesPerSecondToKbps(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return 0;
        }

        return (int)Math.Max(1, bytesPerSecond / 1024L);
    }

    private int ResolveTorrentDownloadLimitKbps(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        if (torrent.DownloadLimit != 0)
        {
            return torrent.DownloadLimit;
        }

        if (!string.IsNullOrWhiteSpace(torrent.Category) && this.categoryService != null)
        {
            var category = this.categoryService.GetByName(torrent.Category);
            if (category?.DefaultDownloadLimit > 0)
            {
                return category.DefaultDownloadLimit;
            }
        }

        return 0;
    }

    private int ResolveTorrentUploadLimitKbps(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        if (torrent.UploadLimit != 0)
        {
            return torrent.UploadLimit;
        }

        if (!string.IsNullOrWhiteSpace(torrent.Category) && this.categoryService != null)
        {
            var category = this.categoryService.GetByName(torrent.Category);
            if (category?.DefaultUploadLimit > 0)
            {
                return category.DefaultUploadLimit;
            }
        }

        return 0;
    }

    public static double GetPriorityWeight(int priority)
    {
        return priority switch
        {
            2 => 2.0,
            0 => 0.5,
            _ => 1.0,
        };
    }
}
