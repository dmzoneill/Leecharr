// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Concurrent;
using NLog;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public interface ITorrentProgressMilestoneService
{
    void CheckProgressMilestones(Torrent torrent, double oldProgress, double newProgress);

    void ClearTorrent(int torrentId);
}

public class TorrentProgressMilestoneService : ITorrentProgressMilestoneService
{
    private static readonly int[] MilestonePercents = { 25, 50, 75, 100 };

    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<int, int> highestMilestonePercentByTorrentId = new();

    public TorrentProgressMilestoneService(IEventAggregator eventAggregator = null)
    {
        this.eventAggregator = eventAggregator;
    }

    public void CheckProgressMilestones(Torrent torrent, double oldProgress, double newProgress)
    {
        if (torrent == null || this.eventAggregator == null)
        {
            return;
        }

        if (newProgress < oldProgress - 0.0001)
        {
            this.ResetHighestMilestone(torrent.Id, newProgress);
        }

        foreach (var milestonePercent in MilestonePercents)
        {
            var threshold = milestonePercent / 100.0;
            if (newProgress + 1e-9 < threshold)
            {
                continue;
            }

            var highest = this.highestMilestonePercentByTorrentId.GetOrAdd(torrent.Id, 0);
            if (highest >= milestonePercent)
            {
                continue;
            }

            this.highestMilestonePercentByTorrentId[torrent.Id] = milestonePercent;
            this.logger.Debug(
                "Torrent #{0} reached progress milestone {1}% (progress {2:P1})",
                torrent.Id,
                milestonePercent,
                newProgress);
            this.eventAggregator.PublishEvent(new TorrentProgressMilestoneEvent(torrent, milestonePercent));
        }
    }

    public void ClearTorrent(int torrentId)
    {
        this.highestMilestonePercentByTorrentId.TryRemove(torrentId, out _);
    }

    private void ResetHighestMilestone(int torrentId, double progress)
    {
        var newHighest = 0;
        foreach (var milestonePercent in MilestonePercents)
        {
            if (progress + 1e-9 >= milestonePercent / 100.0)
            {
                newHighest = milestonePercent;
            }
        }

        if (newHighest > 0)
        {
            this.highestMilestonePercentByTorrentId[torrentId] = newHighest;
        }
        else
        {
            this.highestMilestonePercentByTorrentId.TryRemove(torrentId, out _);
        }
    }
}
