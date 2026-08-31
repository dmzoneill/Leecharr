using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Torrents;

public interface ITorrentService
{
    IEnumerable<Torrent> GetAll();
    Torrent Get(int id);
    Torrent GetByInfoHash(string infoHash);
    Task<Torrent> AddFromParsedTorrentAsync(ParsedTorrent parsed, string category = null, string savePath = null, bool startPaused = false, byte[] rawBytes = null);
    Task<Torrent> AddFromMagnetAsync(string magnetUri, string category = null, string savePath = null, bool startPaused = false);
    Task<Torrent> UpdateAsync(Torrent torrent);
    Task DeleteAsync(int id, bool deleteFiles = false);
    Task PauseAsync(int id);
    Task ResumeAsync(int id);
    Task ForceRecheckAsync(int id);
    Task ForceAnnounceAsync(int id);
    Task MoveQueueAsync(int id, string position);
}
