// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using FluentAssertions;
using Leecharr.Http.Authentication;
using NUnit.Framework;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class AuthRateLimiterTest
{
    [Test]
    public void IsThrottled_WhenNoFailures_ReturnsFalse()
    {
        using var limiter = new AuthRateLimiter(maxFailedAttempts: 3);
        limiter.IsThrottled("192.168.1.1").Should().BeFalse();
    }

    [Test]
    public void RecordFailure_ExceedingThreshold_ThrottlesClient()
    {
        using var limiter = new AuthRateLimiter(maxFailedAttempts: 3, attemptWindow: TimeSpan.FromMinutes(5), lockoutDuration: TimeSpan.FromMinutes(10));
        var ip = "10.0.0.5";

        limiter.RecordFailure(ip);
        limiter.IsThrottled(ip).Should().BeFalse();

        limiter.RecordFailure(ip);
        limiter.IsThrottled(ip).Should().BeFalse();

        limiter.RecordFailure(ip);
        limiter.IsThrottled(ip).Should().BeTrue();
    }

    [Test]
    public void Reset_ClearsFailureCountForClient()
    {
        using var limiter = new AuthRateLimiter(maxFailedAttempts: 2);
        var ip = "10.0.0.6";

        limiter.RecordFailure(ip);
        limiter.RecordFailure(ip);
        limiter.IsThrottled(ip).Should().BeTrue();

        limiter.Reset(ip);
        limiter.IsThrottled(ip).Should().BeFalse();
    }

    [Test]
    public void SweepExpired_RemovesExpiredEntries()
    {
        using var limiter = new AuthRateLimiter(
            maxFailedAttempts: 2,
            attemptWindow: TimeSpan.FromMilliseconds(50),
            lockoutDuration: TimeSpan.FromMilliseconds(50),
            maxCapacity: 100);

        limiter.RecordFailure("1.1.1.1");
        limiter.RecordFailure("2.2.2.2");
        limiter.TrackedIpCount.Should().Be(2);

        Thread.Sleep(100);
        limiter.SweepExpired();

        limiter.TrackedIpCount.Should().Be(0);
    }

    [Test]
    public void RecordFailure_EnforcesMaxCapacityBound()
    {
        using var limiter = new AuthRateLimiter(
            maxFailedAttempts: 5,
            attemptWindow: TimeSpan.FromHours(1),
            lockoutDuration: TimeSpan.FromHours(1),
            maxCapacity: 5);

        for (var i = 1; i <= 10; i++)
        {
            limiter.RecordFailure($"192.168.1.{i}");
        }

        // Capacity bound must be maintained
        limiter.TrackedIpCount.Should().BeLessThanOrEqualTo(5);
    }

    [Test]
    public void Dispose_SafelyDisposesCleanupTimer()
    {
        var limiter = new AuthRateLimiter(maxFailedAttempts: 3, cleanupInterval: TimeSpan.FromMilliseconds(50));
        limiter.RecordFailure("10.0.0.1");

        var act = () => limiter.Dispose();
        act.Should().NotThrow();
    }
}
