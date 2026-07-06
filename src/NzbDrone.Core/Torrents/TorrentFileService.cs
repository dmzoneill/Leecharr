using System.Collections.Generic;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public interface ITorrentFileService
{
    IEnumerable<TorrentFile> GetFiles(int torrentId);
    void SetPriority(int fileId, int priority);
}

public class TorrentFileService : ITorrentFileService
{
    private readonly ITorrentFileRepository _repository;
    private readonly IEventAggregator _eventAggregator;

    public TorrentFileService(ITorrentFileRepository repository, IEventAggregator eventAggregator)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
    }

    public IEnumerable<TorrentFile> GetFiles(int torrentId)
    {
        return _repository.GetByTorrentId(torrentId);
    }

    public void SetPriority(int fileId, int priority)
    {
        var file = _repository.Get(fileId);
        if (file != null)
        {
            file.Priority = priority;
            _repository.Update(file);
        }
    }
}
