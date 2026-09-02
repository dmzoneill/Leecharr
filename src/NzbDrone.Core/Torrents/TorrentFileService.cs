// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public interface ITorrentFileService
{
    IEnumerable<TorrentFile> GetFiles(int torrentId);

    void SetPriority(int fileId, int priority);

    Task SetPriorityAsync(int fileId, int priority);
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

    public void SetPriority(int fileId, int priority)
    {
        this.SetPriorityAsync(fileId, priority).GetAwaiter().GetResult();
    }

    public async Task SetPriorityAsync(int fileId, int priority)
    {
        var file = this.repository.Get(fileId);
        if (file != null)
        {
            file.Priority = priority;
            this.repository.Update(file);
            await this.downloadEngine.SetFilePriorityAsync(file.TorrentId, file.Path, priority);
        }
    }
}
