// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public interface IDownloadEngine
{
    string ProtocolName { get; }

    int DhtNodeCount => 0;

    Task StartAsync();

    Task StopAsync();

    Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null);

    Task RemoveTorrentAsync(int torrentId, bool deleteFiles);

    Task PauseTorrentAsync(int torrentId);

    Task ResumeTorrentAsync(int torrentId);

    Task ForceRecheckAsync(int torrentId);

    Task ForceAnnounceAsync(int torrentId);

    Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers);

    Task SetFilePriorityAsync(int torrentId, string filePath, int priority);

    Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps);

    Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps);

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

    IReadOnlyList<PeerInfo> GetPeers();
}

public class PeerInfo
{
    public string Ip { get; set; }

    public int Port { get; set; }

    public string Client { get; set; }

    public string Flags { get; set; }

    public double Progress { get; set; }

    public long DownloadSpeed { get; set; }

    public long UploadSpeed { get; set; }

    public long Downloaded { get; set; }

    public long Uploaded { get; set; }

    public bool IsEncrypted { get; set; }
}
