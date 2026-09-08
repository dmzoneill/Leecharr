// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public interface IDownloadEngine
{
    string ProtocolName { get; }

    bool IsHaltedByKillSwitch => false;

    int DhtNodeCount => 0;

    Task StartAsync();

    Task StopAsync();

    Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null);

    Task RemoveTorrentAsync(int torrentId, bool deleteFiles);

    Task PauseTorrentAsync(int torrentId);

    Task PauseAllTorrentsAsync() => Task.CompletedTask;

    Task PauseAllAsync() => this.PauseAllTorrentsAsync();

    Task ResumeTorrentAsync(int torrentId);

    Task ResumeAllTorrentsAsync() => Task.CompletedTask;

    Task ResumeAllAsync() => this.ResumeAllTorrentsAsync();

    Task ForceRecheckAsync(int torrentId);

    Task ForceAnnounceAsync(int torrentId);

    Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers);

    Task RemoveTrackersAsync(int torrentId, IEnumerable<string> trackers);

    Task SetFilePriorityAsync(int torrentId, string filePath, int priority);

    Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps);

    Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps);

    Task SetTorrentPrivateStatusAsync(int torrentId, bool isPrivate) => Task.CompletedTask;

    Task SetSuperSeedingAsync(int torrentId, bool enabled) => Task.CompletedTask;

    Task SetSequentialDownloadAsync(int torrentId, bool enabled) => Task.CompletedTask;

    Task SetFirstLastPiecePriorityAsync(int torrentId, bool enabled) => Task.CompletedTask;

    Task<bool> RenameFileAsync(int torrentId, string oldRelativePath, string newRelativePath) => Task.FromResult(false);

    Task<bool> RenameFolderAsync(int torrentId, string oldRelativeFolder, string newRelativeFolder) => Task.FromResult(false);

    Task MoveTorrentFilesAsync(int torrentId, string newSavePath, bool moveFiles = true) => Task.CompletedTask;

    IDownloadTask GetTask(int torrentId);

    IEnumerable<IDownloadTask> GetAllTasks();

    TorrentEngineMetrics GetEngineMetrics() => new();

    TorrentResourceMetrics GetTorrentResourceMetrics(int torrentId) => null;

    IReadOnlyList<TorrentResourceMetrics> GetAllTorrentResourceMetrics() => System.Array.Empty<TorrentResourceMetrics>();

    void CheckTrackerHealth()
    {
    }

    void CheckDiskSpaceHealth()
    {
    }

    Task<EngineHealthCheckResult> ProbeHealthAsync() => Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" });
}

public interface IDownloadTask
{
    int TorrentId { get; }

    string InfoHash { get; }

    string Category => string.Empty;

    TorrentStatus Status { get; }

    long TotalBytes => 0;

    long TotalSize => this.TotalBytes;

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

    TorrentResourceMetrics GetResourceMetrics() => null;

    PiecePicker Picker => null;

    bool IsSuperSeeding => false;

    string ErrorMessage => null;

    bool IsStalled => false;

    bool IsStorageFull => false;

    bool IsOutOfDiskSpace => false;

    int PieceLength => 0;
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

    public bool IsChoked { get; set; }

    public bool IsInterested { get; set; }

    public bool ClientIsChoked { get; set; }

    public bool ClientIsInterested { get; set; }

    public bool IsIncoming { get; set; }

    public bool IsUtp { get; set; }
}
