// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Tags;

namespace Leecharr.Core.Test.Tags;

[TestFixture]
public class TagEffectivePolicyTest
{
    [Test]
    public void Aggregate_WhenMultipleTags_UsesStrictestLimits()
    {
        var tags = new[]
        {
            new Tag
            {
                DownloadLimitKbps = 5000,
                UploadLimitKbps = 8000,
                MinSeedRatio = 2.0,
                MinSeedTimeSeconds = 90,
            },
            new Tag
            {
                DownloadLimitKbps = 3000,
                UploadLimitKbps = 12000,
                MinSeedRatio = 3.5,
                MinSeedTimeSeconds = 3600,
            },
        };

        var result = TagEffectivePolicy.Aggregate(tags);

        result.DownloadLimitKbps.Should().Be(3000);
        result.UploadLimitKbps.Should().Be(8000);
        result.MinSeedRatio.Should().Be(3.5);
        result.MinSeedTimeMinutes.Should().Be(60);
    }
}
