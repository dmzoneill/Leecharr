// Copyright (c) FeedItOut. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags;

public class Tag : ModelBase
{
    public string Label { get; set; }

    public string Color { get; set; }

    public int? UploadLimitKbps { get; set; }

    public int? DownloadLimitKbps { get; set; }

    public double? MinSeedRatio { get; set; }

    public long? MinSeedTimeSeconds { get; set; }
}
