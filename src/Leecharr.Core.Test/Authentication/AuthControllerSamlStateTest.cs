// Copyright (c) FeedItOut. All rights reserved.

using System;
using FluentAssertions;
using Leecharr.Api.V1.Auth;
using NUnit.Framework;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class AuthControllerSamlStateTest
{
    [SetUp]
    public void SetUp()
    {
        AuthController.ResetThrottling();
    }

    [TearDown]
    public void TearDown()
    {
        AuthController.ResetThrottling();
    }

    [Test]
    public void PruneExpiredSamlState_WhenEntriesStillValid_RetainsThem()
    {
        AuthController.RegisterPendingSamlRequest("req_active");
        AuthController.TryRecordSamlAssertion("assert_active", DateTime.UtcNow.AddMinutes(30));

        AuthController.PruneExpiredSamlState(DateTime.UtcNow);

        AuthController.TrackedPendingSamlRequestsCount.Should().Be(1);
        AuthController.TrackedProcessedSamlAssertionsCount.Should().Be(1);
    }

    [Test]
    public void PruneExpiredSamlState_WhenEntriesExpired_RemovesThem()
    {
        AuthController.RegisterPendingSamlRequest("req_expired");
        AuthController.TryRecordSamlAssertion("assert_expired", DateTime.UtcNow.AddMinutes(10));

        AuthController.PruneExpiredSamlState(DateTime.UtcNow.AddMinutes(16));

        AuthController.TrackedPendingSamlRequestsCount.Should().Be(0);
        AuthController.TrackedProcessedSamlAssertionsCount.Should().Be(0);
    }

    [Test]
    public void PruneExpiredSamlState_WhenExceedingCapacity_TrimsPendingRequests()
    {
        for (var i = 0; i < 10; i++)
        {
            AuthController.RegisterPendingSamlRequest($"req_{i:D2}");
        }

        AuthController.TrackedPendingSamlRequestsCount.Should().Be(10);

        AuthController.PruneExpiredSamlState(DateTime.UtcNow, maxTrackedSamlIds: 6);

        AuthController.TrackedPendingSamlRequestsCount.Should().Be(3);
    }

    [Test]
    public void TryRecordSamlAssertion_WhenReplayWindowExceedsFifteenMinutes_RejectsSecondAttempt()
    {
        var replayUntil = DateTime.UtcNow.AddHours(1);
        AuthController.TryRecordSamlAssertion("assert_long_lived", replayUntil).Should().BeTrue();
        AuthController.TryRecordSamlAssertion("assert_long_lived", replayUntil).Should().BeFalse();
    }

    [Test]
    public void TryRecordSamlAssertion_WhenAssertionIdMissing_ReturnsFalse()
    {
        AuthController.TryRecordSamlAssertion(string.Empty, DateTime.UtcNow.AddMinutes(30)).Should().BeFalse();
        AuthController.TryRecordSamlAssertion("   ", DateTime.UtcNow.AddMinutes(30)).Should().BeFalse();
    }

    [Test]
    public void IsPendingSamlRequestValid_WhenExpired_RemovesEntry()
    {
        AuthController.RecordPendingSamlRequestExpiry("req_ttl", DateTime.UtcNow.AddMinutes(-1));
        AuthController.TrackedPendingSamlRequestsCount.Should().Be(1);

        AuthController.IsPendingSamlRequestValid("req_ttl").Should().BeFalse();
        AuthController.TrackedPendingSamlRequestsCount.Should().Be(0);
    }
}
