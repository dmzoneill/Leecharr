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

    Task<Torrent> AddFromParsedTorrentAsync(ParsedTorrent parsed, string category, string savePath, bool startPaused, byte[] rawBytes, bool? sequentialDownload, bool? firstLastPiecePriority);

    Task<Torrent> AddFromMagnetAsync(string magnetUri, string category = null, string savePath = null, bool startPaused = false);

    Task<Torrent> AddFromMagnetAsync(string magnetUri, string category, string savePath, bool startPaused, bool? sequentialDownload, bool? firstLastPiecePriority);

    Task SetSequentialDownloadAsync(int id, bool enabled);

    Task SetFirstLastPiecePriorityAsync(int id, bool enabled);

    Task<Torrent> UpdateAsync(Torrent torrent);

    Task DeleteAsync(int id, bool deleteFiles = false);

    Task PauseAsync(int id);

    Task PauseAsync(int id, string reason);

    Task ResumeAsync(int id);

    Task ForceRecheckAsync(int id);

    Task ForceAnnounceAsync(int id);

    Task MoveQueueAsync(int id, string position);

    Task MoveQueueBatchAsync(IEnumerable<int> ids, string direction);

    NzbDrone.Core.BitTorrent.IDownloadTask GetDownloadTask(int torrentId);

    Task<bool> RenameFileAsync(int id, string oldPath, string newPath);

    Task<bool> RenameFolderAsync(int id, string oldPath, string newPath);

    Task SetSuperSeedingAsync(int id, bool enabled);

    Task SetLocationAsync(int id, string newSavePath, bool moveFiles = true);

    Task SetCategoryAsync(int id, string category);

    int GetEffectiveDownloadLimit(Torrent torrent);

    int GetEffectiveUploadLimit(Torrent torrent);

    double GetEffectiveTargetRatio(Torrent torrent);

    int GetEffectiveTargetSeedTimeMinutes(Torrent torrent);

    int ResolveEffectiveDownloadLimit(int torrentLimit, int categoryLimit);

    int ResolveEffectiveUploadLimit(int torrentLimit, int categoryLimit);

    Task PropagateCategoryLimitsAsync(NzbDrone.Core.Categories.Category category);
}
