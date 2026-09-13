// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace Leecharr.Api.V1.Torrents;

public class TorrentPreviewRequest
{
    public string MagnetLink { get; set; }

    public string MagnetUrl { get; set; }

    public string Uri { get; set; }

    public string TorrentBase64 { get; set; }

    public string DownloadUrl { get; set; }
}

public class TorrentPreviewResource
{
    public string Name { get; set; }

    public string InfoHash { get; set; }

    public long TotalSize { get; set; }

    public int PieceCount { get; set; }

    public int PieceLength { get; set; }

    public string Comment { get; set; }

    public string CreatedBy { get; set; }

    public DateTime? CreationDate { get; set; }

    public List<TorrentPreviewFileResource> Files { get; set; } = new();

    public List<string> Trackers { get; set; } = new();
}

public class TorrentPreviewFileResource
{
    public string Path { get; set; }

    public long Size { get; set; }

    public string Extension { get; set; }
}
