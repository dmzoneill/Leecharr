// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.SignalR;

[TestFixture]
public class TelemetryBroadcasterTest
{
    [Test]
    public void PieceMapSignalREventHandler_BroadcastsWhenConnected()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        var handler = new PieceMapSignalREventHandler(broadcaster);
        handler.Handle(new PieceVerifiedEvent(42, 15));

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "pieceMapUpdated" &&
            m.Body != null));
    }

    [Test]
    public void PieceMapSignalREventHandler_DoesNotBroadcastWhenDisconnected()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(false);

        var handler = new PieceMapSignalREventHandler(broadcaster);
        handler.Handle(new PieceVerifiedEvent(42, 15));

        broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void PieceMapSignalREventHandler_HandlesNullGracefully()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        var handler = new PieceMapSignalREventHandler(broadcaster);
        handler.Handle(null!);

        broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }
}
