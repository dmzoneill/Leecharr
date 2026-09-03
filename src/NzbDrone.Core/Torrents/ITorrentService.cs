// Copyright (c) PlaceholderCompany. All rights reserved.

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

    NzbDrone.Core.BitTorrent.IDownloadTask GetDownloadTask(int torrentId);

    Task<bool> RenameFileAsync(int id, string oldPath, string newPath);

    Task<bool> RenameFolderAsync(int id, string oldPath, string newPath);

    Task SetSuperSeedingAsync(int id, bool enabled);
}
