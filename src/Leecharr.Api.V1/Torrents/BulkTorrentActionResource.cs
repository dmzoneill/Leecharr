// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace Leecharr.Api.V1.Torrents;

public class BulkTorrentActionResource
{
    public List<int> TorrentIds { get; set; } = new();

    public string Action { get; set; } = string.Empty; // "start", "stop", "delete", "recheck", "announce", "setCategory", "addTags", "removeTags", "setPriority", "setSpeedLimits"

    public bool DeleteFiles { get; set; }

    public int? CategoryId { get; set; }

    public string Category { get; set; }

    public List<int> TagIds { get; set; }

    public int? Priority { get; set; }

    public int? UploadLimit { get; set; }

    public int? DownloadLimit { get; set; }
}

public class BulkActionResult
{
    public int SuccessCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Errors { get; set; } = new();

    public List<int> SucceededIds { get; set; } = new();

    public Dictionary<int, string> FailedIds { get; set; } = new();
}
