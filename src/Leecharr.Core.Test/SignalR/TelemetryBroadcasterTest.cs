// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.SignalR;

[TestFixture]
public class TelemetryBroadcasterTest
{
    [TearDown]
    public void TearDown()
    {
        MessageHub.ResetForTesting();
    }

    [Test]
    public void PieceMapSignalREventHandler_BroadcastsWhenConnected()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        using var handler = new PieceMapSignalREventHandler(broadcaster);
        handler.Handle(new PieceVerifiedEvent(42, 15));
        handler.Flush();

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "pieceMapUpdated" &&
            m.Body != null));
    }

    [Test]
    public void PieceMapSignalREventHandler_DoesNotBroadcastWhenDisconnected()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(false);

        using var handler = new PieceMapSignalREventHandler(broadcaster);
        handler.Handle(new PieceVerifiedEvent(42, 15));
        handler.Flush();

        broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void PieceMapSignalREventHandler_HandlesNullGracefully()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        using var handler = new PieceMapSignalREventHandler(broadcaster);
        handler.Handle(null!);
        handler.Flush();

        broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task PieceMapSignalREventHandler_BatchesPiecesAndFlushesPeriodically()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        using var handler = new PieceMapSignalREventHandler(broadcaster, flushIntervalMs: 50);

        // Rapidly emit multiple piece verified events for the same torrent
        handler.Handle(new PieceVerifiedEvent(42, 1));
        handler.Handle(new PieceVerifiedEvent(42, 2));
        handler.Handle(new PieceVerifiedEvent(42, 3));

        // Immediately before timer fires, no broadcasts are sent yet (batched in memory)
        broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());

        // Wait for periodic timer to trigger
        await Task.Delay(150);

        // All 3 pieces were batched into a single broadcast message
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "pieceMapUpdated" &&
            m.Body != null));
    }

    [Test]
    public async Task SignalRMessageBroadcaster_UsesBoundedChannelWithDropOldestUnderHighLoad()
    {
        MessageHub.AddConnectionForTesting();

        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        var tcs = new TaskCompletionSource();
        clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => tcs.Task);

        // Create broadcaster with telemetry capacity 3
        using var broadcaster = new SignalRMessageBroadcaster(hubContext, telemetryCapacity: 3);

        // Broadcast first telemetry message so worker picks it up and blocks on tcs.Task
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
        await Task.Delay(50);

        // Push 5 more telemetry messages while worker is blocked
        for (int i = 1; i <= 5; i++)
        {
            broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
        }

        // Telemetry channel capacity is 3 and configured with DropOldest, so count cannot exceed 3
        broadcaster.TelemetryChannel.Reader.Count.Should().BeLessThanOrEqualTo(3);

        // Unblock worker to clean up
        tcs.SetResult();
    }

    [Test]
    public async Task SignalRMessageBroadcaster_GuaranteedEvents_AreSentToGuaranteedChannel()
    {
        MessageHub.AddConnectionForTesting();

        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        var tcs = new TaskCompletionSource();
        clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => tcs.Task);

        // Create broadcaster with telemetry capacity 3
        using var broadcaster = new SignalRMessageBroadcaster(hubContext, telemetryCapacity: 3);

        // Broadcast first domain message so worker picks it up and blocks on tcs.Task
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "TorrentStatusChanged" });
        await Task.Delay(50);

        // Push 5 more domain messages while worker is blocked
        for (int i = 1; i <= 5; i++)
        {
            broadcaster.BroadcastMessage(new SignalRMessage { Name = $"TorrentDownloadCompleted_{i}" });
        }

        // Guaranteed channel does not drop oldest; all 5 pending messages remain in the queue
        broadcaster.GuaranteedChannel.Reader.Count.Should().Be(5);

        // Unblock worker to clean up
        tcs.SetResult();
    }

    [Test]
    public void PieceMapSignalREventHandler_DisposeDuringFlush_DoesNotThrow()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        for (int i = 0; i < 20; i++)
        {
            var handler = new PieceMapSignalREventHandler(broadcaster, flushIntervalMs: 5);
            for (int p = 0; p < 50; p++)
            {
                handler.Handle(new PieceVerifiedEvent(1, p));
            }

            // Dispose immediately while periodic timer may be triggering or active
            Action act = () => handler.Dispose();
            act.Should().NotThrow();
        }
    }
}
