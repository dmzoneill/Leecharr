// Copyright (c) FeedItOut. All rights reserved.

using System;
using FluentAssertions;
using Leecharr.Api.V1.System;
using NUnit.Framework;

namespace Leecharr.Core.Test.SystemTasks;

[TestFixture]
public class SystemRunDurationFormatterTest
{
    [TestCase(2, "00:00:02")]
    [TestCase(125, "00:02:05")]
    [TestCase(86400, "24:00:00")]
    [TestCase(90000, "25:00:00")]
    public void Format_TimeSpan_UsesTotalHoursNotModulo24(int totalSeconds, string expected)
    {
        SystemRunDurationFormatter.Format(TimeSpan.FromSeconds(totalSeconds)).Should().Be(expected);
    }

    [Test]
    public void Format_NegativeSpan_PreservesSign()
    {
        SystemRunDurationFormatter.Format(TimeSpan.FromHours(-1)).Should().Be("-01:00:00");
    }

    [Test]
    public void Format_DateRange_WhenEndBeforeStart_ReturnsNegativeDuration()
    {
        var start = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(-1);
        SystemRunDurationFormatter.Format(start, end).Should().Be("-01:00:00");
    }
}
