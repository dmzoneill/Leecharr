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
    private readonly ITorrentFileRepository _repository;
    private readonly IDownloadEngine _downloadEngine;
    private readonly IEventAggregator _eventAggregator;

    public TorrentFileService(
        ITorrentFileRepository repository,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator)
    {
        _repository = repository;
        _downloadEngine = downloadEngine;
        _eventAggregator = eventAggregator;
    }

    public IEnumerable<TorrentFile> GetFiles(int torrentId)
    {
        return _repository.GetByTorrentId(torrentId);
    }

    public void SetPriority(int fileId, int priority)
    {
        SetPriorityAsync(fileId, priority).GetAwaiter().GetResult();
    }

    public async Task SetPriorityAsync(int fileId, int priority)
    {
        var file = _repository.Get(fileId);
        if (file != null)
        {
            file.Priority = priority;
            _repository.Update(file);
            await _downloadEngine.SetFilePriorityAsync(file.TorrentId, file.Path, priority);
        }
    }
}
