// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Nzbget;

public interface INzbgetDownloadQueueState
{
    bool DownloadPaused { get; }

    Task PauseDownloadAsync(ITorrentService torrentService);

    Task ResumeDownloadAsync(ITorrentService torrentService);
}

/// <summary>
/// Tracks NZBGet queue-level download pause separately from per-torrent GroupPause state.
/// </summary>
public sealed class NzbgetDownloadQueueState : INzbgetDownloadQueueState
{
    public static INzbgetDownloadQueueState Default { get; } = new NzbgetDownloadQueueState();

    private readonly object gate = new();
    private readonly HashSet<int> queuePausedTorrentIds = new();
    private bool downloadPaused;

    public bool DownloadPaused
    {
        get
        {
            lock (this.gate)
            {
                return this.downloadPaused;
            }
        }
    }

    public async Task PauseDownloadAsync(ITorrentService torrentService)
    {
        List<int> toPause;
        lock (this.gate)
        {
            if (this.downloadPaused)
            {
                return;
            }

            this.downloadPaused = true;
            this.queuePausedTorrentIds.Clear();
            toPause = torrentService.GetAll()
                .Where(t => t.Status is not TorrentStatus.Paused and not TorrentStatus.Stopped)
                .Select(t => t.Id)
                .ToList();
            foreach (var id in toPause)
            {
                this.queuePausedTorrentIds.Add(id);
            }
        }

        foreach (var id in toPause)
        {
            await torrentService.PauseAsync(id);
        }
    }

    public async Task ResumeDownloadAsync(ITorrentService torrentService)
    {
        List<int> toResume;
        lock (this.gate)
        {
            this.downloadPaused = false;
            toResume = this.queuePausedTorrentIds.ToList();
            this.queuePausedTorrentIds.Clear();
        }

        foreach (var id in toResume)
        {
            await torrentService.ResumeAsync(id);
        }
    }
}
