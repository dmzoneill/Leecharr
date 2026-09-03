// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class Torrent : ModelBase
{
    public string Name { get; set; }

    public string InfoHash { get; set; }

    public long TotalSize { get; set; }

    public int PieceCount { get; set; }

    public int PieceLength { get; set; }

    public string Comment { get; set; }

    public string CreatedBy { get; set; }

    public DateTime? CreationDate { get; set; }

    public bool IsPrivate { get; set; }

    public TorrentStatus Status { get; set; }

    public long Downloaded { get; set; }

    public long Uploaded { get; set; }

    public double Ratio { get; set; }

    public double Progress { get; set; }

    public long DownloadSpeed { get; set; }

    public long UploadSpeed { get; set; }

    public long Eta { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public string SavePath { get; set; }

    public string Category { get; set; }

    public string Label { get; set; }

    public string TrackerUrl { get; set; }

    public int Priority { get; set; }

    public int QueuePosition { get; set; }

    public int DownloadLimit { get; set; }

    public int UploadLimit { get; set; }

    public bool SequentialDownload { get; set; }

    public bool InitialSeeding { get; set; }

    public bool ForceStart { get; set; }

    public double TargetRatio { get; set; }

    public int TargetSeedTimeMinutes { get; set; }

    public string ShareLimitAction { get; set; } = "Pause";

    public DateTime DateAdded { get; set; }

    public DateTime? DateCompleted { get; set; }

    public DateTime? LastActive { get; set; }

    public List<int> TagIds { get; set; } = new();

    public int SeedTimeMinutes => this.DateCompleted.HasValue ? (int)Math.Max(0, (DateTime.UtcNow - this.DateCompleted.Value).TotalMinutes) : 0;
}
