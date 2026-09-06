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

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class SystemTaskControllerTest
{
    private IManageCommandQueue commandQueueManager = null!;
    private IScheduledTaskRepository scheduledTaskRepository = null!;
    private SystemTaskController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.commandQueueManager = Substitute.For<IManageCommandQueue>();
        this.scheduledTaskRepository = Substitute.For<IScheduledTaskRepository>();
        this.controller = new SystemTaskController(this.commandQueueManager, this.scheduledTaskRepository);
    }

    [Test]
    public void GetTasks_WhenNoTasksInDb_ReturnsDefaultTasks()
    {
        this.scheduledTaskRepository.All().Returns(new List<ScheduledTask>());

        var result = this.controller.GetTasks();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var tasks = okResult.Value as List<ScheduledTaskResource>;
        tasks.Should().NotBeNull();
        tasks!.Count.Should().Be(6);
        tasks.Should().Contain(t => t.TypeName == "WatchFolderScanTask");
    }

    [Test]
    public void ExecuteTask_WhenDbTaskExists_PushesCommandWithResolvedName()
    {
        var task = new ScheduledTask { Id = 1, TypeName = "WatchFolderScanTask" };
        this.scheduledTaskRepository.Get(1).Returns(task);

        var result = this.controller.ExecuteTask(1);
        result.Should().BeOfType<OkObjectResult>();

        this.commandQueueManager.Received(1).PushRaw("WatchFolderScan", "{}", CommandTrigger.Manual);
    }

    [Test]
    public void ExecuteTask_WhenDbTaskNull_PushesFallbackTaskName()
    {
        this.scheduledTaskRepository.Get(3).Returns((ScheduledTask)null!);

        var result = this.controller.ExecuteTask(3);
        result.Should().BeOfType<OkObjectResult>();

        this.commandQueueManager.Received(1).PushRaw("VpnKillSwitchCheck", "{}", CommandTrigger.Manual);
    }
}
