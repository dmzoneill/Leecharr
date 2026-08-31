// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;
using NzbDrone.SignalR;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SignalRHubIntegrationTest : IntegrationTestBase
{
    private HubConnection connection = null!;

    [SetUp]
    public void SetUp()
    {
        this.connection = new HubConnectionBuilder()
            .WithUrl($"{GlobalSetup.Factory.BaseUrl}/signalr/messages", options =>
            {
                if (!string.IsNullOrEmpty(this.ApiKey))
                {
                    options.Headers.Add("X-Api-Key", this.ApiKey);
                }
            })
            .WithAutomaticReconnect()
            .Build();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (this.connection != null)
        {
            try
            {
                await this.connection.StopAsync();
                await this.connection.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    [Test]
    public async Task Connect_ReceivesInitialVersionMessage()
    {
        var tcs = new TaskCompletionSource<SignalRMessage>();

        this.connection.On<SignalRMessage>("receiveMessage", msg =>
        {
            if (msg.Name == "version")
            {
                tcs.TrySetResult(msg);
            }
        });

        await this.connection.StartAsync();
        this.connection.State.Should().Be(HubConnectionState.Connected);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => tcs.TrySetCanceled());

        var received = await tcs.Task;
        received.Should().NotBeNull();
        received.Name.Should().Be("version");
        received.Body.Should().NotBeNull();
    }

    [Test]
    public async Task BroadcastMessage_DeliversEventToConnectedClients()
    {
        var tcs = new TaskCompletionSource<SignalRMessage>();

        this.connection.On<SignalRMessage>("receiveMessage", msg =>
        {
            if (string.Equals(msg.Name, "testBroadcast", StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(msg);
            }
        });

        await this.connection.StartAsync();
        this.connection.State.Should().Be(HubConnectionState.Connected);

        var broadcaster = GlobalSetup.Factory.Services.GetService(typeof(IBroadcastSignalRMessage)) as IBroadcastSignalRMessage;
        broadcaster.Should().NotBeNull();

        broadcaster!.BroadcastMessage(new SignalRMessage
        {
            Name = "testBroadcast",
            Body = new { message = "hello signalr" },
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => tcs.TrySetCanceled());

        var received = await tcs.Task;
        received.Should().NotBeNull();
        received.Name.Should().Be("testBroadcast");
    }
}
