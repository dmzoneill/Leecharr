// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace Leecharr.Api.V1.TrackerBoost;

public class AddTrackerResource
{
    public string Url { get; set; } = string.Empty;
}

public class InjectTrackerResource
{
    public int TorrentId { get; set; }

    public string InfoHash { get; set; } = string.Empty;

    public string TrackerUrl { get; set; } = string.Empty;

    public bool Force { get; set; }
}

public class BulkImportTrackersResource
{
    public string TrackersText { get; set; } = string.Empty;
}
