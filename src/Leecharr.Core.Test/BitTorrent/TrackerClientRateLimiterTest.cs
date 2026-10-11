// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class TrackerClientRateLimiterTest
{
    [Test]
    public void IsRateLimited_WhenOverLimit_ReturnsTrue()
    {
        var limiter = new TrackerClientRateLimiter();
        var clientIp = IPAddress.Parse("198.51.100.9");

        limiter.IsRateLimited(clientIp, 2).Should().BeFalse();
        limiter.IsRateLimited(clientIp, 2).Should().BeFalse();
        limiter.IsRateLimited(clientIp, 2).Should().BeTrue();
    }

    [Test]
    public void IsRateLimited_WhenLimitDisabled_ReturnsFalse()
    {
        var limiter = new TrackerClientRateLimiter();
        var clientIp = IPAddress.Parse("198.51.100.10");

        for (var i = 0; i < 5; i++)
        {
            limiter.IsRateLimited(clientIp, 0).Should().BeFalse();
        }
    }
}
