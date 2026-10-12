// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Tags;

public static class TagEffectivePolicy
{
    public readonly struct Aggregated
    {
        public int DownloadLimitKbps { get; init; }

        public int UploadLimitKbps { get; init; }

        public double MinSeedRatio { get; init; }

        public int MinSeedTimeMinutes { get; init; }
    }

    public static Aggregated Aggregate(IEnumerable<Tag> tags)
    {
        var downloadLimitKbps = 0;
        var uploadLimitKbps = 0;
        var minSeedRatio = 0d;
        var minSeedTimeSeconds = 0L;

        if (tags == null)
        {
            return default;
        }

        foreach (var tag in tags)
        {
            if (tag == null)
            {
                continue;
            }

            if (tag.DownloadLimitKbps > 0)
            {
                downloadLimitKbps = downloadLimitKbps == 0
                    ? tag.DownloadLimitKbps.Value
                    : Math.Min(downloadLimitKbps, tag.DownloadLimitKbps.Value);
            }

            if (tag.UploadLimitKbps > 0)
            {
                uploadLimitKbps = uploadLimitKbps == 0
                    ? tag.UploadLimitKbps.Value
                    : Math.Min(uploadLimitKbps, tag.UploadLimitKbps.Value);
            }

            if (tag.MinSeedRatio > 0)
            {
                minSeedRatio = minSeedRatio <= 0
                    ? tag.MinSeedRatio.Value
                    : Math.Max(minSeedRatio, tag.MinSeedRatio.Value);
            }

            if (tag.MinSeedTimeSeconds > 0)
            {
                minSeedTimeSeconds = minSeedTimeSeconds == 0
                    ? tag.MinSeedTimeSeconds.Value
                    : Math.Max(minSeedTimeSeconds, tag.MinSeedTimeSeconds.Value);
            }
        }

        var minSeedTimeMinutes = minSeedTimeSeconds > 0
            ? (int)((minSeedTimeSeconds + 59) / 60)
            : 0;

        return new Aggregated
        {
            DownloadLimitKbps = downloadLimitKbps,
            UploadLimitKbps = uploadLimitKbps,
            MinSeedRatio = minSeedRatio,
            MinSeedTimeMinutes = minSeedTimeMinutes,
        };
    }
}
