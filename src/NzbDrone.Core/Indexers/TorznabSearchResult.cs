using System;

namespace NzbDrone.Core.Indexers;

public class TorznabSearchResult
{
    public string Title { get; set; }
    public string Guid { get; set; }
    public string DownloadUrl { get; set; }
    public string MagnetUrl { get; set; }
    public string InfoHash { get; set; }
    public long Size { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public double DownloadVolumeFactor { get; set; } = 1.0;
    public double UploadVolumeFactor { get; set; } = 1.0;
    public bool IsFreeleech => DownloadVolumeFactor == 0.0;
    public string Category { get; set; }
    public DateTime PublishDate { get; set; } = DateTime.UtcNow;
    public string IndexerName { get; set; }
    public int IndexerId { get; set; }
}
