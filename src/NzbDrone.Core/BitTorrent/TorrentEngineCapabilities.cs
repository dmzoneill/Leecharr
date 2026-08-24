namespace NzbDrone.Core.BitTorrent;

public class TorrentEngineCapabilities
{
    public bool SupportsUtp { get; set; }
    public bool SupportsDht { get; set; }
    public bool SupportsPex { get; set; }
    public bool SupportsLpd { get; set; }
    public bool SupportsV2Torrents { get; set; }
    public bool SupportsSequentialDownload { get; set; }
    public bool SupportsFastResume { get; set; }
    public bool SupportsCustomPiecePickers { get; set; }
    public bool SupportsDynamicRateLimits { get; set; }
    public bool SupportsSparseAllocation { get; set; }
    public bool SupportsMemoryMappedIo { get; set; }
    public bool SupportsEncryptionToggle { get; set; }
}
