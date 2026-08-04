using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IDownloadHistoryService
{
    List<DownloadHistory> GetAll(string query = null, string status = null, int limit = 500);
    DownloadHistory Get(int id);
    DownloadHistory GetByInfoHash(string infoHash);
    void Delete(int id);
    void ClearAll();
    DownloadHistory RecordTorrentAdded(
        Torrent torrent,
        string source = null,
        string magnetUrl = null,
        string downloadUrl = null,
        string indexerName = null);
    void RecordTorrentUpdated(Torrent torrent);
    void RecordTorrentRemoved(Torrent torrent, string reason = "Deleted from library");
    Torrent ReAdd(int historyId);
    void Update(DownloadHistory history);
    int ReconcileAllTorrents();
}
