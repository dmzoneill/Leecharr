// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using Leecharr.Http.Security;
using NUnit.Framework;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class RpcSessionStoreTest
{
    [SetUp]
    public void SetUp()
    {
        RpcSessionStore.InvalidateAllSessions();
    }

    [Test]
    public void SetSession_WithValidToken_IsValidReturnsTrue()
    {
        var store = new RpcSessionStore();
        store.SetSession("token-123", TimeSpan.FromMinutes(10));

        store.IsValid("token-123").Should().BeTrue();
        store.IsValid("invalid-token").Should().BeFalse();
        store.IsValid(string.Empty).Should().BeFalse();
        store.IsValid(null).Should().BeFalse();
    }

    [Test]
    public void Clear_RemovesAllSessionsFromStore()
    {
        var store = new RpcSessionStore();
        store.SetSession("token-1", TimeSpan.FromMinutes(10));
        store.SetSession("token-2", TimeSpan.FromMinutes(10));

        store.Count.Should().Be(2);
        store.Clear();

        store.Count.Should().Be(0);
        store.IsValid("token-1").Should().BeFalse();
        store.IsValid("token-2").Should().BeFalse();
    }

    [Test]
    public void InvalidateAllSessions_ClearsSessionsAcrossAllStoreInstances()
    {
        var store1 = new RpcSessionStore();
        var store2 = new RpcSessionStore();

        store1.SetSession("token-store1", TimeSpan.FromMinutes(10));
        store2.SetSession("token-store2", TimeSpan.FromMinutes(10));

        store1.IsValid("token-store1").Should().BeTrue();
        store2.IsValid("token-store2").Should().BeTrue();

        RpcSessionStore.InvalidateAllSessions();

        store1.IsValid("token-store1").Should().BeFalse();
        store2.IsValid("token-store2").Should().BeFalse();
        store1.Count.Should().Be(0);
        store2.Count.Should().Be(0);
    }

    [Test]
    public void PruneExpired_RemovesExpiredSessions()
    {
        var store = new RpcSessionStore();
        store.SetSession("active-token", TimeSpan.FromMinutes(10));
        store.SetSession("expired-token", DateTime.UtcNow.AddMinutes(-5));

        store.IsValid("expired-token").Should().BeFalse();
        store.IsValid("active-token").Should().BeTrue();

        store.PruneExpired();
        store.Count.Should().Be(1);
    }

    [Test]
    public void MaxCapacity_EvictsOldestSession()
    {
        var store = new RpcSessionStore(10);
        for (var i = 0; i < 10; i++)
        {
            store.SetSession($"token-{i}", DateTime.UtcNow.AddMinutes(i + 1));
        }

        store.Count.Should().Be(10);

        store.SetSession("token-overflow", DateTime.UtcNow.AddMinutes(20));
        store.Count.Should().Be(10);
        store.IsValid("token-0").Should().BeFalse();
        store.IsValid("token-overflow").Should().BeTrue();
    }

    [Test]
    public void RemoveSession_RemovesSpecificSession()
    {
        var store = new RpcSessionStore();
        store.SetSession("token-a", TimeSpan.FromMinutes(10));
        store.SetSession("token-b", TimeSpan.FromMinutes(10));

        store.RemoveSession("token-a").Should().BeTrue();
        store.IsValid("token-a").Should().BeFalse();
        store.IsValid("token-b").Should().BeTrue();
    }

    [Test]
    public void TryGetValue_ReturnsExpiryAndValidity()
    {
        var store = new RpcSessionStore();
        var expiryTime = DateTime.UtcNow.AddMinutes(15);
        store.SetSession("token-val", expiryTime);

        store.TryGetValue("token-val", out var retrievedExpiry).Should().BeTrue();
        retrievedExpiry.Should().BeCloseTo(expiryTime, TimeSpan.FromSeconds(1));

        store.TryGetValue("nonexistent", out _).Should().BeFalse();
    }
}
