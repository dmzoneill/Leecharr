using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public interface IDownloadEngine
{
    string ProtocolName { get; }
    Task StartAsync();
    Task StopAsync();
    Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null);
    Task RemoveTorrentAsync(int torrentId, bool deleteFiles);
    Task PauseTorrentAsync(int torrentId);
    Task ResumeTorrentAsync(int torrentId);
    Task ForceRecheckAsync(int torrentId);
    Task ForceAnnounceAsync(int torrentId);
    IDownloadTask GetTask(int torrentId);
    IEnumerable<IDownloadTask> GetAllTasks();
}

public interface IDownloadTask
{
    int TorrentId { get; }
    string InfoHash { get; }
    TorrentStatus Status { get; }
    long DownloadedBytes { get; }
    long UploadedBytes { get; }
    double Progress { get; }
    long DownloadSpeed { get; }
    long UploadSpeed { get; }
    int ConnectedSeeders { get; }
    int ConnectedLeechers { get; }
    bool[] PieceBitfield { get; }
    int[] PieceAvailability { get; }
}
