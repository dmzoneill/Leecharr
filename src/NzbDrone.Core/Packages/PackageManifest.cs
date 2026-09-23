// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Packages;

public class PackageManifest
{
    public int SchemaVersion { get; set; } = 1;

    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    public string Client { get; set; } = "Leecharr";

    public List<PackageTorrentItem> Torrents { get; set; } = new();
}

public class PackageTorrentItem
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string InfoHash { get; set; }

    public string Category { get; set; }

    public List<string> Tags { get; set; } = new();

    public List<PackageTrackerItem> Trackers { get; set; } = new();

    public long TotalSize { get; set; }

    public int PieceCount { get; set; }

    public int PieceLength { get; set; }

    public bool IsPrivate { get; set; }
}

public class PackageTrackerItem
{
    public string Url { get; set; }

    public int Tier { get; set; }

    public string Status { get; set; }

    public bool Enabled { get; set; } = true;

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int TotalAnnounces { get; set; }

    public int SuccessfulAnnounces { get; set; }

    public DateTime? LastAnnounce { get; set; }
}
