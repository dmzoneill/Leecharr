using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface IDownloadHistoryRepository : IBasicRepository<DownloadHistory>
{
    DownloadHistory FindByInfoHash(string infoHash);
    DownloadHistory FindByTorrentId(int torrentId);
    List<DownloadHistory> GetHistory(string query = null, string status = null, int limit = 500);
    void DeleteAll();
}
