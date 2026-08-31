// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Indexers;

public class IndexerResource : RestResource
{
    public string Name { get; set; }

    public string Implementation { get; set; } = "Torznab";

    public string ConfigContract { get; set; }

    public string Settings { get; set; }

    public bool Enable { get; set; } = true;

    public int Priority { get; set; } = 1;

    public string Url { get; set; }

    public string ApiKey { get; set; } = string.Empty;

    public List<int> Categories { get; set; } = new();

    public bool EnableRss { get; set; } = true;

    public bool EnableSearch { get; set; } = true;

    public bool FreeleechOnly { get; set; }

    public int MinSeeders { get; set; } = 1;

    public int DownloadClientId { get; set; }

    public List<int> Tags { get; set; } = new();
}

public class IndexerTestResult
{
    public bool Success { get; set; }

    public string Message { get; set; }
}

public class DownloadReleaseRequest
{
    public string Title { get; set; }

    public string DownloadUrl { get; set; }

    public string MagnetUrl { get; set; }

    public string InfoHash { get; set; }

    public string Category { get; set; }

    public string SavePath { get; set; }

    public bool StartPaused { get; set; }
}

public class ReleaseInfoResource
{
    public string Title { get; set; }

    public string Guid { get; set; }

    public string Link { get; set; }

    public string Comments { get; set; }

    public DateTime PublishDate { get; set; }

    public string Category { get; set; }

    public long Size { get; set; }

    public string DownloadUrl { get; set; }

    public string MagnetUrl { get; set; }

    public string InfoHash { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int IndexerId { get; set; }

    public string IndexerName { get; set; }

    public double DownloadVolumeFactor { get; set; } = 1.0;

    public double UploadVolumeFactor { get; set; } = 1.0;

    public bool IsFreeleech => this.DownloadVolumeFactor <= 0.0;
}

public class IndexerSearchRequest
{
    public string Query { get; set; }

    public string Category { get; set; }

    public int? IndexerId { get; set; }

    public bool FreeleechOnly { get; set; }

    public int? Season { get; set; }

    public int? Ep { get; set; }

    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    public int Offset { get; set; } = 0;

    public int Limit { get; set; } = 50;

    public string Type { get; set; }
}
