// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Seeding;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.Seeding;

[TestFixture]
public class SpeedScheduleControllerTest
{
    private ISpeedScheduleRepository repository = null!;
    private ISpeedSchedulerService schedulerService = null!;
    private IBroadcastSignalRMessage signalRBroadcaster = null!;
    private SpeedScheduleController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<ISpeedScheduleRepository>();
        this.schedulerService = Substitute.For<ISpeedSchedulerService>();
        this.signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        this.signalRBroadcaster.IsConnected.Returns(true);

        this.controller = new SpeedScheduleController(
            this.repository,
            this.schedulerService,
            this.signalRBroadcaster);
    }

    [Test]
    public void GetAll_ReturnsAllSchedules()
    {
        var schedules = new List<SpeedSchedule>
        {
            new()
            {
                Id = 1,
                Name = "Night Throttling",
                Days = 127,
                StartTime = "01:00:00",
                EndTime = "06:00:00",
                MaxDownloadSpeed = 1000,
                MaxUploadSpeed = 500,
                IsEnabled = true,
                Priority = 1,
            },
            new()
            {
                Id = 2,
                Name = "Work Hours",
                Days = 62,
                StartTime = "09:00:00",
                EndTime = "17:00:00",
                MaxDownloadSpeed = 200,
                MaxUploadSpeed = 100,
                IsEnabled = false,
                Priority = 2,
            },
        };

        this.repository.All().Returns(schedules);

        var actionResult = this.controller.GetAll();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeOfType<List<SpeedScheduleResource>>().Subject;

        resources.Should().HaveCount(2);
        resources[0].Id.Should().Be(1);
        resources[0].Name.Should().Be("Night Throttling");
        resources[1].Id.Should().Be(2);
        resources[1].Name.Should().Be("Work Hours");
    }

    [Test]
    public void GetById_WhenExists_ReturnsOkWithResource()
    {
        var schedule = new SpeedSchedule
        {
            Id = 5,
            Name = "Weekend Boost",
            Days = 65,
            StartTime = "00:00:00",
            EndTime = "23:59:59",
            MaxDownloadSpeed = 10000,
            MaxUploadSpeed = 5000,
            IsEnabled = true,
            Priority = 0,
        };

        this.repository.Get(5).Returns(schedule);

        var actionResult = this.controller.GetById(5);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resource = okResult.Value.Should().BeOfType<SpeedScheduleResource>().Subject;

        resource.Id.Should().Be(5);
        resource.Name.Should().Be("Weekend Boost");
        resource.Days.Should().Be(65);
    }

    [Test]
    public void GetById_WhenNotFound_ReturnsNotFound()
    {
        this.repository.Get(99).Returns((SpeedSchedule)null!);

        var actionResult = this.controller.GetById(99);
        actionResult.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void GetActiveLimits_ReturnsCurrentSchedulerLimits()
    {
        this.schedulerService.GetCurrentLimits().Returns(new EffectiveSpeedLimits
        {
            MaxDownloadSpeedKbps = 2500,
            MaxUploadSpeedKbps = 1200,
            IsThrottled = true,
            IsPaused = false,
        });

        var actionResult = this.controller.GetActiveLimits();
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var limits = okResult.Value.Should().BeOfType<SpeedLimitsResource>().Subject;

        limits.MaxDownloadSpeedKbps.Should().Be(2500);
        limits.MaxUploadSpeedKbps.Should().Be(1200);
        limits.IsThrottled.Should().BeTrue();
        limits.IsPaused.Should().BeFalse();
    }

    [Test]
    public async Task Create_WhenValid_InsertsAndAppliesLimits()
    {
        var inputResource = new SpeedScheduleResource
        {
            Name = "New Schedule",
            Days = 127,
            StartTime = "02:00:00",
            EndTime = "07:00:00",
            MaxDownloadSpeed = 5000,
            MaxUploadSpeed = 2500,
            IsEnabled = true,
            Priority = 1,
        };

        this.repository.Insert(Arg.Any<SpeedSchedule>()).Returns(callInfo =>
        {
            var m = callInfo.Arg<SpeedSchedule>();
            m.Id = 10;
            return m;
        });

        var actionResult = await this.controller.Create(inputResource);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var created = okResult.Value.Should().BeOfType<SpeedScheduleResource>().Subject;

        created.Id.Should().Be(10);
        created.Name.Should().Be("New Schedule");
        await this.schedulerService.Received(1).ApplyCurrentLimitsAsync();
    }

    [Test]
    public async Task Create_WhenNullOrInvalidTime_ReturnsBadRequest()
    {
        var nullResult = await this.controller.Create(null!);
        nullResult.Result.Should().BeOfType<BadRequestResult>();

        var invalidTimeResource = new SpeedScheduleResource
        {
            Name = "Bad Time",
            StartTime = "invalid-time",
            EndTime = "23:59:59",
        };

        var invalidResult = await this.controller.Create(invalidTimeResource);
        invalidResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task Update_WhenValid_UpdatesAndAppliesLimits()
    {
        var existing = new SpeedSchedule
        {
            Id = 3,
            Name = "Old Name",
            Days = 127,
            StartTime = "00:00:00",
            EndTime = "12:00:00",
        };

        this.repository.Get(3).Returns(existing);

        var updateResource = new SpeedScheduleResource
        {
            Name = "Updated Name",
            Days = 62,
            StartTime = "08:00:00",
            EndTime = "18:00:00",
            MaxDownloadSpeed = 3000,
            MaxUploadSpeed = 1500,
            IsEnabled = true,
            Priority = 2,
        };

        var actionResult = await this.controller.Update(3, updateResource);
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var updated = okResult.Value.Should().BeOfType<SpeedScheduleResource>().Subject;

        updated.Id.Should().Be(3);
        updated.Name.Should().Be("Updated Name");
        this.repository.Received(1).Update(Arg.Is<SpeedSchedule>(s => s.Id == 3 && s.Name == "Updated Name"));
        await this.schedulerService.Received(1).ApplyCurrentLimitsAsync();
    }

    [Test]
    public async Task Update_WhenNotFound_ReturnsNotFound()
    {
        this.repository.Get(404).Returns((SpeedSchedule)null!);

        var updateResource = new SpeedScheduleResource
        {
            Name = "Nonexistent",
            StartTime = "00:00:00",
            EndTime = "23:59:59",
        };

        var actionResult = await this.controller.Update(404, updateResource);
        actionResult.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task Update_WhenInvalidTime_ReturnsBadRequest()
    {
        var updateResource = new SpeedScheduleResource
        {
            Name = "Bad Time",
            StartTime = "invalid",
            EndTime = "12:00:00",
        };

        var actionResult = await this.controller.Update(1, updateResource);
        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task Delete_DeletesAndAppliesLimits()
    {
        var actionResult = await this.controller.Delete(7);
        actionResult.Should().BeOfType<OkResult>();

        this.repository.Received(1).Delete(7);
        await this.schedulerService.Received(1).ApplyCurrentLimitsAsync();
    }

    [Test]
    public void Handle_WhenCreatedEvent_BroadcastsSpeedscheduleAdded()
    {
        var schedule = new SpeedSchedule
        {
            Id = 15,
            Name = "Night Boost",
            Days = 127,
            StartTime = "01:00:00",
            EndTime = "05:00:00",
            MaxDownloadSpeed = 8000,
            MaxUploadSpeed = 4000,
            IsEnabled = true,
            Priority = 1,
        };

        var modelEvent = new ModelEvent<SpeedSchedule>(schedule, ModelAction.Created);
        this.controller.Handle(modelEvent);

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(msg =>
            msg.Name == "speedscheduleAdded" &&
            msg.Action == ModelAction.Created &&
            msg.Body is SpeedScheduleResource &&
            ((SpeedScheduleResource)msg.Body).Id == 15 &&
            ((SpeedScheduleResource)msg.Body).Name == "Night Boost"));
    }

    [Test]
    public void Handle_WhenUpdatedEvent_BroadcastsSpeedscheduleUpdated()
    {
        var schedule = new SpeedSchedule
        {
            Id = 20,
            Name = "Updated Schedule",
            Days = 62,
            StartTime = "08:00:00",
            EndTime = "17:00:00",
            MaxDownloadSpeed = 2000,
            MaxUploadSpeed = 1000,
            IsEnabled = false,
            Priority = 3,
        };

        var modelEvent = new ModelEvent<SpeedSchedule>(schedule, ModelAction.Updated);
        this.controller.Handle(modelEvent);

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(msg =>
            msg.Name == "speedscheduleUpdated" &&
            msg.Action == ModelAction.Updated &&
            msg.Body is SpeedScheduleResource &&
            ((SpeedScheduleResource)msg.Body).Id == 20 &&
            ((SpeedScheduleResource)msg.Body).Name == "Updated Schedule"));
    }

    [Test]
    public void Handle_WhenDeletedEvent_BroadcastsSpeedscheduleDeleted()
    {
        var schedule = new SpeedSchedule
        {
            Id = 25,
            Name = "Deleted Schedule",
        };

        var modelEvent = new ModelEvent<SpeedSchedule>(schedule, ModelAction.Deleted);
        this.controller.Handle(modelEvent);

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(msg =>
            msg.Name == "speedscheduleDeleted" &&
            msg.Action == ModelAction.Deleted &&
            msg.Body is SpeedScheduleResource &&
            ((SpeedScheduleResource)msg.Body).Id == 25));
    }

    [Test]
    public void Handle_WhenNotConnected_DoesNotBroadcast()
    {
        this.signalRBroadcaster.IsConnected.Returns(false);

        var schedule = new SpeedSchedule { Id = 30, Name = "Test" };
        var modelEvent = new ModelEvent<SpeedSchedule>(schedule, ModelAction.Created);
        this.controller.Handle(modelEvent);

        this.signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Handle_WhenNullMessage_DoesNotThrow()
    {
        var action = () => this.controller.Handle(null!);
        action.Should().NotThrow();
    }
}
