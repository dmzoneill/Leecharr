// Copyright (c) FeedItOut. All rights reserved.

using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;

namespace Leecharr.Core.Test.Extensions;

[TestFixture]
public class DateTimeExtensionsTest
{
    [Test]
    public void ToUnixTimeSecondsAsUtc_TreatsUnspecifiedAsUtc()
    {
        var stored = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Unspecified);
        var expected = new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc)).ToUnixTimeSeconds();

        stored.ToUnixTimeSecondsAsUtc().Should().Be(expected);
        stored.ToUnixTimeSecondsAsUtc().Should().Be(1784109600L);
    }

    [Test]
    public void ToUnixTimeSecondsAsUtc_PreservesUtcKind()
    {
        var utc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        utc.ToUnixTimeSecondsAsUtc().Should().Be(new DateTimeOffset(utc).ToUnixTimeSeconds());
    }
}
