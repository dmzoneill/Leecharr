// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;

namespace Leecharr.Core.Test.SystemTasks;

[TestFixture]
public class SystemTaskControllerTest
{
    private IManageCommandQueue commandQueueManager;
    private IScheduledTaskRepository scheduledTaskRepository;
    private ITaskManager taskManager;

    [SetUp]
    public void SetUp()
    {
        this.commandQueueManager = Substitute.For<IManageCommandQueue>();
        this.scheduledTaskRepository = Substitute.For<IScheduledTaskRepository>();
        this.taskManager = Substitute.For<ITaskManager>();
    }

    [Test]
    public void GetTasks_ReturnsGenuineTasksFromTaskManager()
    {
        var now = DateTime.UtcNow;
        this.taskManager.GetAll().Returns(new List<ScheduledTask>
        {
            new ScheduledTask
            {
                Id = 1,
                TypeName = "WatchFolderScanTask",
                Interval = 10,
                LastExecution = now.AddMinutes(-5),
                LastStartTime = now.AddMinutes(-5).AddSeconds(-2),
            },
        });

        var controller = new SystemTaskController(this.commandQueueManager, this.scheduledTaskRepository, this.taskManager);
        var actionResult = controller.GetTasks();

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = actionResult.Result as OkObjectResult;
        var list = okResult.Value as List<ScheduledTaskResource>;

        list.Should().NotBeNull();
        list.Should().HaveCount(1);
        list[0].Id.Should().Be(1);
        list[0].TypeName.Should().Be("WatchFolderScanTask");
        list[0].Name.Should().Be("WatchFolderScan");
        list[0].Interval.Should().Be(10);
        list[0].LastExecution.Should().Be(now.AddMinutes(-5));
        list[0].LastDuration.Should().Be("00:00:02");
    }

    [Test]
    public void GetTasks_ReturnsEmptyList_WhenNoTasksInDatabase()
    {
        this.taskManager.GetAll().Returns(new List<ScheduledTask>());

        var controller = new SystemTaskController(this.commandQueueManager, this.scheduledTaskRepository, this.taskManager);
        var actionResult = controller.GetTasks();

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = actionResult.Result as OkObjectResult;
        var list = okResult.Value as List<ScheduledTaskResource>;

        list.Should().NotBeNull();
        list.Should().BeEmpty();
    }
}
