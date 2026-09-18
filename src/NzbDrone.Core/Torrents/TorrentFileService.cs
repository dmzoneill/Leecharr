// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public interface ITorrentFileService
{
    IEnumerable<TorrentFile> GetFiles(int torrentId);

    Dictionary<int, List<TorrentFile>> GetFilesForTorrents(IEnumerable<int> torrentIds);

    void SetPriority(int fileId, int priority);

    Task SetPriorityAsync(int fileId, int priority);

    Task<bool> SetPriorityAsync(int torrentId, int fileId, int priority);

    Task<bool> SetPrioritiesAsync(int torrentId, IEnumerable<(int FileId, int Priority)> priorities);
}

public class TorrentFileService : ITorrentFileService
{
    private readonly ITorrentFileRepository repository;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;

    public TorrentFileService(
        ITorrentFileRepository repository,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator)
    {
        this.repository = repository;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
    }

    public IEnumerable<TorrentFile> GetFiles(int torrentId)
    {
        return this.repository.GetByTorrentId(torrentId);
    }

    public Dictionary<int, List<TorrentFile>> GetFilesForTorrents(IEnumerable<int> torrentIds)
    {
        return this.repository.GetByTorrentIds(torrentIds);
    }

    public void SetPriority(int fileId, int priority)
    {
        this.SetPriorityAsync(fileId, priority).GetAwaiter().GetResult();
    }

    public async Task SetPriorityAsync(int fileId, int priority)
    {
        var file = this.repository.Get(fileId);
        if (file != null)
        {
            var clampedPriority = Math.Clamp(priority, 0, 7);
            file.Priority = clampedPriority;
            this.repository.Update(file);
            if (this.downloadEngine != null)
            {
                await this.downloadEngine.SetFilePriorityAsync(file.TorrentId, file.Path, clampedPriority);
            }
        }
    }

    public async Task<bool> SetPriorityAsync(int torrentId, int fileId, int priority)
    {
        var file = this.repository.Get(fileId);
        if (file == null || file.TorrentId != torrentId)
        {
            return false;
        }

        var clampedPriority = Math.Clamp(priority, 0, 7);
        file.Priority = clampedPriority;
        this.repository.Update(file);
        if (this.downloadEngine != null)
        {
            await this.downloadEngine.SetFilePriorityAsync(file.TorrentId, file.Path, clampedPriority);
        }

        return true;
    }

    public async Task<bool> SetPrioritiesAsync(int torrentId, IEnumerable<(int FileId, int Priority)> priorities)
    {
        if (priorities == null)
        {
            return false;
        }

        var allOk = true;
        foreach (var (fileId, priority) in priorities)
        {
            var success = await this.SetPriorityAsync(torrentId, fileId, priority);
            if (!success)
            {
                allOk = false;
            }
        }

        return allOk;
    }
}
