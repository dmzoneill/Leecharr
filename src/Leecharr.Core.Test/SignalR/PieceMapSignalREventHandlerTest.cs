// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.SignalR;

[TestFixture]
public class PieceMapSignalREventHandlerTest
{
    private IBroadcastSignalRMessage broadcaster;

    [SetUp]
    public void SetUp()
    {
        this.broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        this.broadcaster.IsConnected.Returns(true);
    }

    [Test]
    public void Handle_WhenBroadcasterNullOrDisconnected_DoesNotThrowOrBroadcast()
    {
        this.broadcaster.IsConnected.Returns(false);

        using var handler = new PieceMapSignalREventHandler(this.broadcaster);
        handler.Handle(new PieceVerifiedEvent(1, 10));
        handler.Flush();

        this.broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Handle_WhenDisposed_DoesNotRecordPieces()
    {
        var handler = new PieceMapSignalREventHandler(this.broadcaster);
        handler.Dispose();

        handler.Handle(new PieceVerifiedEvent(1, 10));
        handler.Flush();

        this.broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Flush_BatchesMultiplePiecesForSameTorrent()
    {
        using var handler = new PieceMapSignalREventHandler(this.broadcaster);
        handler.Handle(new PieceVerifiedEvent(1, 5));
        handler.Handle(new PieceVerifiedEvent(1, 2));
        handler.Handle(new PieceVerifiedEvent(1, 8));

        handler.Flush();

        this.broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "pieceMapUpdated" &&
            m.Body != null));
    }

    [Test]
    public void Flush_WhenNoPendingPieces_DoesNotBroadcast()
    {
        using var handler = new PieceMapSignalREventHandler(this.broadcaster);
        handler.Flush();

        this.broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task TimerCallback_WhenBroadcasterThrowsException_CatchesAndLogsWithoutCrashing()
    {
        this.broadcaster.When(b => b.BroadcastMessage(Arg.Any<SignalRMessage>()))
            .Do(_ => throw new InvalidOperationException("Simulated SignalR broadcast transient failure"));

        using var handler = new PieceMapSignalREventHandler(this.broadcaster, flushIntervalMs: 20);

        handler.Handle(new PieceVerifiedEvent(1, 1));
        handler.Handle(new PieceVerifiedEvent(1, 2));

        // Wait for timer to fire and execute callback that triggers exception
        await Task.Delay(100);

        // Ensure no unhandled exception crashed the process / thread pool
        Action act = () => handler.Handle(new PieceVerifiedEvent(1, 3));
        act.Should().NotThrow();
    }

    [Test]
    public async Task TimerCallback_WhenDisposed_DoesNotBroadcast()
    {
        var handler = new PieceMapSignalREventHandler(this.broadcaster, flushIntervalMs: 20);
        handler.Dispose();

        this.broadcaster.ClearReceivedCalls();

        await Task.Delay(60);

        this.broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Dispose_WhenFlushThrowsException_CatchesWithoutThrowing()
    {
        this.broadcaster.When(b => b.BroadcastMessage(Arg.Any<SignalRMessage>()))
            .Do(_ => throw new InvalidOperationException("Simulated failure during dispose flush"));

        var handler = new PieceMapSignalREventHandler(this.broadcaster);
        handler.Handle(new PieceVerifiedEvent(1, 10));

        Action act = () => handler.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_FlushesRemainingPendingPieces()
    {
        var handler = new PieceMapSignalREventHandler(this.broadcaster, flushIntervalMs: 10000);
        handler.Handle(new PieceVerifiedEvent(1, 10));

        // Dispose should flush pending pieces
        handler.Dispose();

        this.broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "pieceMapUpdated"));
    }

    [Test]
    public void CompressToRanges_WhenEmptyOrNull_ReturnsEmptyList()
    {
        PieceMapSignalREventHandler.CompressToRanges(null).Should().BeEmpty();
        PieceMapSignalREventHandler.CompressToRanges(new List<int>()).Should().BeEmpty();
    }

    [Test]
    public void CompressToRanges_WhenSinglePiece_ReturnsSingleRange()
    {
        var ranges = PieceMapSignalREventHandler.CompressToRanges(new[] { 42 });
        ranges.Should().HaveCount(1);
        ranges[0].Should().Equal(42, 42);
    }

    [Test]
    public void CompressToRanges_WhenContiguousPieces_CompressesToOneRange()
    {
        var ranges = PieceMapSignalREventHandler.CompressToRanges(new[] { 0, 1, 2, 3, 4, 5 });
        ranges.Should().HaveCount(1);
        ranges[0].Should().Equal(0, 5);
    }

    [Test]
    public void CompressToRanges_WhenDiscontinuousPieces_CompressesToMultipleRanges()
    {
        var ranges = PieceMapSignalREventHandler.CompressToRanges(new[] { 0, 1, 2, 10, 11, 20 });
        ranges.Should().HaveCount(3);
        ranges[0].Should().Equal(0, 2);
        ranges[1].Should().Equal(10, 11);
        ranges[2].Should().Equal(20, 20);
    }
}
