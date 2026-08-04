using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class DownloadHistory : ModelBase
{
    public int? TorrentId { get; set; }
    public string Title { get; set; }
    public string InfoHash { get; set; }
    public long TotalSize { get; set; }
    public DateTime DateAdded { get; set; }
    public DateTime? DateCompleted { get; set; }
    public DateTime? DateRemoved { get; set; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public double Ratio { get; set; }
    public long SeedingTime { get; set; }
    public string PrimaryTracker { get; set; }
    public string IndexerName { get; set; }
    public string Source { get; set; }
    public string MagnetUrl { get; set; }
    public string DownloadUrl { get; set; }
    public string Status { get; set; } = "Active";
    public string RemovalReason { get; set; }
    public string DataJson { get; set; }
}
