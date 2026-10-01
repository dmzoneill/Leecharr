// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public abstract class BaseDaemonDownloadTask : IDownloadTask
{
    private long downloadSpeed;
    private long uploadSpeed;
    private int connectedSeeders;
    private int connectedLeechers;
    private IReadOnlyList<PeerInfo> peers = Array.Empty<PeerInfo>();

    protected BaseDaemonDownloadTask(int torrentId, string infoHash, string name, long totalSize, string category = null)
    {
        this.TorrentId = torrentId;
        this.InfoHash = infoHash;
        this.Name = name;
        this.TotalSize = totalSize;
        this.Category = category ?? string.Empty;
    }

    public int TorrentId { get; }

    public string InfoHash { get; }

    public string Name { get; }

    public string Category { get; set; } = string.Empty;

    public long TotalSize { get; }

    public long TotalBytes => this.TotalSize;

    public TorrentStatus Status { get; set; } = TorrentStatus.Downloading;

    public long DownloadedBytes { get; set; }

    public long UploadedBytes { get; set; }

    public double Progress { get; set; }

    public long DownloadSpeed
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.downloadSpeed;
        set => this.downloadSpeed = value;
    }

    public long UploadSpeed
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.uploadSpeed;
        set => this.uploadSpeed = value;
    }

    public int ConnectedSeeders
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.connectedSeeders;
        set => this.connectedSeeders = value;
    }

    public int ConnectedLeechers
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.connectedLeechers;
        set => this.connectedLeechers = value;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by IDownloadTask interface")]
    public bool[] PieceBitfield { get; set; } = Array.Empty<bool>();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by IDownloadTask interface")]
    public int[] PieceAvailability { get; set; } = Array.Empty<int>();

    public TorrentResourceMetrics GetResourceMetrics() => new()
    {
        TorrentId = this.TorrentId,
        InfoHash = this.InfoHash ?? string.Empty,
        Name = this.Name ?? string.Empty,
        Category = this.Category ?? string.Empty,
        Status = this.Status.ToString() ?? "Stopped",
        Progress = this.Progress,
        TotalBytes = this.TotalSize,
        DownloadedPayload = this.DownloadedBytes,
        UploadedPayload = this.UploadedBytes,
        PayloadDownloadSpeed = this.DownloadSpeed,
        PayloadUploadSpeed = this.UploadSpeed,
        ConnectedSeeds = this.ConnectedSeeders,
        ConnectedLeechers = this.ConnectedLeechers,
        ConnectedPeers = this.ConnectedSeeders + this.ConnectedLeechers,
    };

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        return this.peers;
    }

    public void SetPeers(IEnumerable<PeerInfo> peerList)
    {
        this.peers = peerList?.ToList() ?? (IReadOnlyList<PeerInfo>)Array.Empty<PeerInfo>();
    }
}
