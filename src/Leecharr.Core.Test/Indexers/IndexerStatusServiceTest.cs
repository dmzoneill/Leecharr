// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class IndexerStatusServiceTest
{
    private IndexerStatusService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new IndexerStatusService();
    }

    [Test]
    public void IsDisabled_ShouldReturnFalseInitially()
    {
        this.service.IsDisabled(1).Should().BeFalse();
    }

    [Test]
    public void RecordFailure_ShouldDisableIndexerWithExponentialBackoff()
    {
        this.service.RecordFailure(1, (int)HttpStatusCode.TooManyRequests, "Rate limited");

        this.service.IsDisabled(1).Should().BeTrue();
        var status = this.service.GetStatus(1);
        status.ConsecutiveFailures.Should().Be(1);
        status.LastStatusCode.Should().Be(429);
        status.DisabledTill.Should().NotBeNull();
    }

    [Test]
    public void RecordSuccess_ShouldResetIndexerFailuresAndRecover()
    {
        this.service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Service Unavailable");
        this.service.IsDisabled(1).Should().BeTrue();

        this.service.RecordSuccess(1);
        this.service.IsDisabled(1).Should().BeFalse();
        var status = this.service.GetStatus(1);
        status.ConsecutiveFailures.Should().Be(0);
        status.DisabledTill.Should().BeNull();
    }

    [Test]
    public void CalculateBackoff_ScalesExponentially()
    {
        this.service.CalculateBackoff(1).Should().Be(TimeSpan.FromMinutes(5));
        this.service.CalculateBackoff(2).Should().Be(TimeSpan.FromMinutes(15));
        this.service.CalculateBackoff(3).Should().Be(TimeSpan.FromMinutes(30));
        this.service.CalculateBackoff(4).Should().Be(TimeSpan.FromHours(1));
        this.service.CalculateBackoff(5).Should().Be(TimeSpan.FromHours(2));
        this.service.CalculateBackoff(6).Should().Be(TimeSpan.FromHours(4));
        this.service.CalculateBackoff(7).Should().Be(TimeSpan.FromHours(8));
        this.service.CalculateBackoff(8).Should().Be(TimeSpan.FromHours(24));
        this.service.CalculateBackoff(10).Should().Be(TimeSpan.FromHours(24));
    }
}
