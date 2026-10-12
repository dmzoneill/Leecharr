// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Leecharr.Api.V1.SignalR;
using NUnit.Framework;

namespace Leecharr.Core.Test.Security;

[TestFixture]
public class SignalRHubClientCallableMethodsTest
{
    [TestCase(typeof(NotificationHub), "SubscribeToTopic", "UnsubscribeFromTopic", "SubscribeToLevel", "UnsubscribeFromLevel")]
    [TestCase(typeof(TorrentHub), "SubscribeToTorrent", "UnsubscribeFromTorrent", "SubscribeToAllTorrents", "UnsubscribeFromAllTorrents")]
    public void DedicatedHubs_OnlyExposeSubscriptionMethodsToClients(Type hubType, params string[] allowedMethods)
    {
        var allowed = new HashSet<string>(allowedMethods, StringComparer.Ordinal);

        var clientCallableMethods = hubType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToList();

        clientCallableMethods.Should().BeEquivalentTo(allowed, $"{hubType.Name} must not expose server broadcast helpers as public hub methods");
    }

    [Test]
    public void BroadcastHelpers_AreNotDeclaredOnHubTypes()
    {
        var forbiddenOnNotificationHub = new[]
        {
            nameof(NotificationHubBroadcaster.DispatchNotification),
            nameof(NotificationHubBroadcaster.DispatchNotificationToTopic),
            nameof(NotificationHubBroadcaster.DispatchSystemAlert),
        };

        var forbiddenOnTorrentHub = new[]
        {
            nameof(TorrentHubBroadcaster.BroadcastTorrentProgress),
            nameof(TorrentHubBroadcaster.BroadcastGlobalProgress),
            nameof(TorrentHubBroadcaster.PushSwarmTelemetry),
            nameof(TorrentHubBroadcaster.BroadcastSwarmTelemetry),
        };

        typeof(NotificationHub).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .Should()
            .NotContain(forbiddenOnNotificationHub);

        typeof(TorrentHub).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .Should()
            .NotContain(forbiddenOnTorrentHub);
    }
}
