// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Torrents;

public class TorrentResource : RestResource
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

    public string Status { get; set; }

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

    public int? Priority { get; set; }

    public int? QueuePosition { get; set; }

    public int? DownloadLimit { get; set; }

    public int? UploadLimit { get; set; }

    public bool? SequentialDownload { get; set; }

    public bool? InitialSeeding { get; set; }

    public bool? ForceStart { get; set; }

    public double TargetRatio { get; set; }

    public int TargetSeedTimeMinutes { get; set; }

    public DateTime DateAdded { get; set; }

    public DateTime? DateCompleted { get; set; }

    public DateTime? LastActive { get; set; }

    public List<int> TagIds { get; set; } = new();

    // Enriched Media Fields
    public string MediaTitle { get; set; }

    public int? MediaYear { get; set; }

    public string MediaOverview { get; set; }

    public string PosterUrl { get; set; }

    public string BackdropUrl { get; set; }

    public string Resolution { get; set; }

    public string VideoCodec { get; set; }

    public string AudioCodec { get; set; }

    public string AudioChannels { get; set; }

    public string HdrFormat { get; set; }

    public double? MediaRating { get; set; }
}
