// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.BitTorrent;

public class TorrentEngineMetrics
{
    public string EngineId { get; set; } = "MonoTorrent";

    public string DisplayName { get; set; } = "MonoTorrent (Pure .NET)";

    public string Version { get; set; } = "3.0.2";

    public bool IsRunning { get; set; }

    public int ActiveTorrents { get; set; }

    public int DownloadingTorrents { get; set; }

    public int SeedingTorrents { get; set; }

    public int PausedTorrents { get; set; }

    public long TotalDownloadSpeed { get; set; }

    public long TotalUploadSpeed { get; set; }

    public long TotalProtocolDownloadSpeed { get; set; }

    public long TotalProtocolUploadSpeed { get; set; }

    public long TotalDataDownloaded { get; set; }

    public long TotalDataUploaded { get; set; }

    public long TotalProtocolDownloaded { get; set; }

    public long TotalProtocolUploaded { get; set; }

    public double ProtocolOverheadPercentage { get; set; }

    public int OpenConnections { get; set; }

    public int HalfOpenConnections { get; set; }

    public int MaxConnections { get; set; }

    public int ConnectedSeeds { get; set; }

    public int ConnectedLeechers { get; set; }

    public int TotalSwarmPeers { get; set; }

    public int DhtNodeCount { get; set; }

    public string DhtState { get; set; } = "Ready";

    public long DiskCacheBytesAllocated { get; set; }

    public long DiskCacheCapacityBytes { get; set; }

    public double DiskCacheHitRatio { get; set; }

    public long DiskCacheHits { get; set; }

    public long DiskCacheMisses { get; set; }

    public int DiskPendingWrites { get; set; }

    public int DiskPendingReads { get; set; }

    public long DiskTotalBytesWritten { get; set; }

    public long DiskTotalBytesRead { get; set; }

    public long DiskWriteRate { get; set; }

    public long DiskReadRate { get; set; }

    public double PiecesHashedPerSec { get; set; }

    public long HashFailsTotal { get; set; }

    public int EncryptedConnectionsCount { get; set; }

    public int PlaintextConnectionsCount { get; set; }

    public int UtpConnectionsCount { get; set; }

    public int TcpConnectionsCount { get; set; }

    public long BlockedPeersCount { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
