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
    [Test]
    public void Dispose_WhenCalled_CompletesGracefullyWithoutThrowing()
    {
        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "testMessage" });

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_WhenCalledMultipleTimes_IsIdempotent()
    {
        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        broadcaster.Dispose();

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public async Task Dispose_WhenBroadcastingInFlight_CompletesCleanlyWithoutObjectDisposedException()
    {
        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        var tcs = new TaskCompletionSource();
        clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var broadcaster = new SignalRMessageBroadcaster(hubContext, telemetryCapacity: 10);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "speedPulse" });
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "guaranteedEvent" });

        await Task.Delay(50);

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void BroadcastMessage_AfterDispose_IsIgnoredAndDoesNotThrow()
    {
        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        var broadcaster = new SignalRMessageBroadcaster(hubContext);
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
        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new OperationCanceledException()));

        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "regularMessage" });

        await Task.Delay(50);

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public async Task ProcessChannelAsync_WhenSendThrowsObjectDisposedException_ExitsGracefully()
    {
        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new ObjectDisposedException("MessageHub")));

        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        broadcaster.BroadcastMessage(new SignalRMessage { Name = "regularMessage" });

        await Task.Delay(50);

        Action act = () => broadcaster.Dispose();
        act.Should().NotThrow();
    }
}
