// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.BitTorrent;

public class TorrentResourceMetrics
{
    private string infoHash = string.Empty;
    private string name = string.Empty;
    private string category = string.Empty;
    private string status = "Stopped";

    public int TorrentId { get; set; }

    public string InfoHash
    {
        get => this.infoHash;
        set => this.infoHash = value ?? string.Empty;
    }

    public string Name
    {
        get => this.name;
        set => this.name = value ?? string.Empty;
    }

    public string Category
    {
        get => this.category;
        set => this.category = value ?? string.Empty;
    }

    public string Status
    {
        get => this.status;
        set => this.status = value ?? "Stopped";
    }

    public double Progress { get; set; }

    public long TotalBytes { get; set; }

    public long PayloadDownloadSpeed { get; set; }

    public long PayloadUploadSpeed { get; set; }

    public long ProtocolDownloadSpeed { get; set; }

    public long ProtocolUploadSpeed { get; set; }

    public long DownloadedPayload { get; set; }

    public long UploadedPayload { get; set; }

    public long ProtocolDownloaded { get; set; }

    public long ProtocolUploaded { get; set; }

    public double EfficiencyRatio { get; set; }

    public int ConnectedPeers { get; set; }

    public int ConnectedSeeds { get; set; }

    public int ConnectedLeechers { get; set; }

    public int TotalAvailablePeers { get; set; }

    public int TcpPeers { get; set; }

    public int UtpPeers { get; set; }

    public int EncryptedPeers { get; set; }

    public int PlaintextPeers { get; set; }

    public int TotalPieces { get; set; }

    public int CompletedPieces { get; set; }

    public int PiecesInFlight { get; set; }

    public int PieceLength { get; set; }

    public int HashFails { get; set; }

    public long WastedBytes { get; set; }

    public int DiskPendingWrites { get; set; }

    public long EstimatedMemoryBufferBytes { get; set; }

    public double SwarmAvailability { get; set; }

    public double Ratio { get; set; }

    public long? EtaSeconds { get; set; }
}
