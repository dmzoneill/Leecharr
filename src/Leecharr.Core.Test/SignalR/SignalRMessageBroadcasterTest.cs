// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.SignalR;

[TestFixture]
public class SignalRMessageBroadcasterTest
{
    private IHubContext<MessageHub> hubContext;
    private IHubClients clients;
    private IClientProxy clientProxy;

    [SetUp]
    public void SetUp()
    {
        MessageHub.ResetForTesting();

        this.hubContext = Substitute.For<IHubContext<MessageHub>>();
        this.clients = Substitute.For<IHubClients>();
        this.clientProxy = Substitute.For<IClientProxy>();
        this.hubContext.Clients.Returns(this.clients);
        this.clients.All.Returns(this.clientProxy);
    }

    [TearDown]
    public void TearDown()
    {
        MessageHub.ResetForTesting();
    }

    [Test]
    public void BroadcastMessage_WhenNotConnected_DoesNotEnqueueTelemetryOrGuaranteedMessages()
    {
        MessageHub.ResetForTesting();
        using var broadcaster = new SignalRMessageBroadcaster(this.hubContext);

        broadcaster.IsConnected.Should().BeFalse();

        broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "pieceMapUpdated" });
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "TorrentStatusChanged" });

        broadcaster.TelemetryChannel.Reader.Count.Should().Be(0);
        broadcaster.GuaranteedChannel.Reader.Count.Should().Be(0);

        this.clientProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default, default, default);
    }

    [Test]
    public async Task BroadcastMessage_WhenConnected_EnqueuesTelemetryAndGuaranteedMessages()
    {
        MessageHub.AddConnectionForTesting();
        var tcs = new TaskCompletionSource();
        this.clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => tcs.Task);

        using var broadcaster = new SignalRMessageBroadcaster(this.hubContext, telemetryCapacity: 10);

        broadcaster.IsConnected.Should().BeTrue();

        // Broadcast first message so worker picks it up and blocks on tcs.Task
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
        await Task.Delay(50);

        // Broadcast additional messages while worker is blocked
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "TorrentStatusChanged" });

        broadcaster.TelemetryChannel.Reader.Count.Should().BeGreaterThan(0);
        broadcaster.GuaranteedChannel.Reader.Count.Should().BeGreaterThan(0);

        tcs.SetResult();
    }

    [Test]
    public void BroadcastMessage_WhenMessageIsNull_DoesNotThrowOrEnqueue()
    {
        MessageHub.AddConnectionForTesting();
        using var broadcaster = new SignalRMessageBroadcaster(this.hubContext);

        Action act = () => broadcaster.BroadcastMessage(null);
        act.Should().NotThrow();

        broadcaster.TelemetryChannel.Reader.Count.Should().Be(0);
        broadcaster.GuaranteedChannel.Reader.Count.Should().Be(0);
    }

    [Test]
    public void Dispose_WhenCalled_CompletesGracefullyWithoutThrowing()
    {
        MessageHub.AddConnectionForTesting();
        var broadcaster = new SignalRMessageBroadcaster(this.hubContext);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "testMessage" });

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_WhenCalledMultipleTimes_IsIdempotent()
    {
        var broadcaster = new SignalRMessageBroadcaster(this.hubContext);
        broadcaster.Dispose();

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public async Task Dispose_WhenBroadcastingInFlight_CompletesCleanlyWithoutObjectDisposedException()
    {
        MessageHub.AddConnectionForTesting();
        var tcs = new TaskCompletionSource();
        this.clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var broadcaster = new SignalRMessageBroadcaster(this.hubContext, telemetryCapacity: 10);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "guaranteedEvent" });

        await Task.Delay(50);

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void BroadcastMessage_AfterDispose_IsIgnoredAndDoesNotThrow()
    {
        MessageHub.AddConnectionForTesting();
        var broadcaster = new SignalRMessageBroadcaster(this.hubContext);
        broadcaster.Dispose();

        Action act = () =>
        {
            broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
            broadcaster.BroadcastMessage(new SignalRMessage { Name = "regularMessage" });
        };
        act.Should().NotThrow();
    }

    [Test]
    public async Task ProcessChannelAsync_WhenSendThrowsOperationCanceledException_ExitsGracefully()
    {
        MessageHub.AddConnectionForTesting();
        this.clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new OperationCanceledException()));

        var broadcaster = new SignalRMessageBroadcaster(this.hubContext);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "regularMessage" });

        await Task.Delay(50);

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public async Task ProcessChannelAsync_WhenSendThrowsObjectDisposedException_ExitsGracefully()
    {
        MessageHub.AddConnectionForTesting();
        this.clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new ObjectDisposedException("MessageHub")));

        var broadcaster = new SignalRMessageBroadcaster(this.hubContext);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "regularMessage" });

        await Task.Delay(50);

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }
}
