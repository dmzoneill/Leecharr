// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Seeding;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;
using NzbDrone.SignalR;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SpeedScheduleControllerTests : IntegrationTestBase
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
    public async Task SpeedScheduleCrud_BroadcastsSignalRMessages()
    {
        var addedTcs = new TaskCompletionSource<SignalRMessage>();
        var updatedTcs = new TaskCompletionSource<SignalRMessage>();
        var deletedTcs = new TaskCompletionSource<SignalRMessage>();

        this.connection.On<SignalRMessage>("receiveMessage", msg =>
        {
            if (msg.Name == "speedscheduleAdded")
            {
                addedTcs.TrySetResult(msg);
            }
            else if (msg.Name == "speedscheduleUpdated")
            {
                updatedTcs.TrySetResult(msg);
            }
            else if (msg.Name == "speedscheduleDeleted")
            {
                deletedTcs.TrySetResult(msg);
            }
        });

        await this.connection.StartAsync();
        this.connection.State.Should().Be(HubConnectionState.Connected);

        // 1. Create schedule
        var newSchedule = new SpeedScheduleResource
        {
            Name = "Integration Schedule",
            Days = 127,
            StartTime = "01:30:00",
            EndTime = "05:30:00",
            MaxDownloadSpeed = 15000,
            MaxUploadSpeed = 8000,
            IsEnabled = true,
            Priority = 1,
        };

        var postResponse = await this.PostJsonAsync("/api/v1/speedschedule", newSchedule);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = Deserialize<SpeedScheduleResource>(await postResponse.Content.ReadAsStringAsync());
        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("Integration Schedule");

        // Verify SignalR message for added
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            cts.Token.Register(() => addedTcs.TrySetCanceled());
            var addedMsg = await addedTcs.Task;
            addedMsg.Should().NotBeNull();
            addedMsg.Name.Should().Be("speedscheduleAdded");
        }

        // 2. Get by Id
        var getResponse = await this.GetJsonAsync<SpeedScheduleResource>($"/api/v1/speedschedule/{created.Id}");
        getResponse.Should().NotBeNull();
        getResponse.Name.Should().Be("Integration Schedule");

        // 3. Update schedule
        created.Name = "Integration Schedule Updated";
        created.MaxDownloadSpeed = 20000;
        var putResponse = await this.PutJsonAsync($"/api/v1/speedschedule/{created.Id}", created);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = Deserialize<SpeedScheduleResource>(await putResponse.Content.ReadAsStringAsync());
        updated.Name.Should().Be("Integration Schedule Updated");

        // Verify SignalR message for updated
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            cts.Token.Register(() => updatedTcs.TrySetCanceled());
            var updatedMsg = await updatedTcs.Task;
            updatedMsg.Should().NotBeNull();
            updatedMsg.Name.Should().Be("speedscheduleUpdated");
        }

        // 4. Delete schedule
        var deleteResponse = await this.DeleteAsync($"/api/v1/speedschedule/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify SignalR message for deleted
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            cts.Token.Register(() => deletedTcs.TrySetCanceled());
            var deletedMsg = await deletedTcs.Task;
            deletedMsg.Should().NotBeNull();
            deletedMsg.Name.Should().Be("speedscheduleDeleted");
        }

        // 5. Verify deleted returns 404
        var getDeletedResponse = await this.GetAsync($"/api/v1/speedschedule/{created.Id}");
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetActiveLimits_ReturnsActiveLimits()
    {
        var limits = await this.GetJsonAsync<SpeedLimitsResource>("/api/v1/speedschedule/active");
        limits.Should().NotBeNull();
    }
}
