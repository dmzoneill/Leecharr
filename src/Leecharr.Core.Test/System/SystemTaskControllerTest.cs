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

    [Test]
    [TestCase("WatchFolderScanTask", typeof(NzbDrone.Core.WatchFolder.WatchFolderScanCommand))]
    [TestCase("WatchFolderScan", typeof(NzbDrone.Core.WatchFolder.WatchFolderScanCommand))]
    [TestCase("RssSyncTask", typeof(NzbDrone.Core.Indexers.RssSyncCommand))]
    [TestCase("RssSync", typeof(NzbDrone.Core.Indexers.RssSyncCommand))]
    [TestCase("VpnKillSwitchCheckTask", typeof(NzbDrone.Core.Network.VpnKillSwitchCheckCommand))]
    [TestCase("VpnKillSwitchCheck", typeof(NzbDrone.Core.Network.VpnKillSwitchCheckCommand))]
    [TestCase("ProwlarrSyncTask", typeof(NzbDrone.Core.Indexers.ProwlarrSyncCommand))]
    [TestCase("ProwlarrSync", typeof(NzbDrone.Core.Indexers.ProwlarrSyncCommand))]
    [TestCase("BackupTask", typeof(NzbDrone.Core.Backup.BackupCommand))]
    [TestCase("Backup", typeof(NzbDrone.Core.Backup.BackupCommand))]
    [TestCase("BlocklistUpdateTask", typeof(NzbDrone.Core.Network.Blocklist.BlocklistUpdateCommand))]
    [TestCase("BlocklistUpdate", typeof(NzbDrone.Core.Network.Blocklist.BlocklistUpdateCommand))]
    [TestCase("SessionCleanupTask", typeof(NzbDrone.Core.Authentication.SessionCleanupCommand))]
    [TestCase("SessionCleanup", typeof(NzbDrone.Core.Authentication.SessionCleanupCommand))]
    [TestCase("GeoIpUpdateTask", typeof(NzbDrone.Core.Network.GeoIp.GeoIpUpdateCommand))]
    [TestCase("GeoIpUpdate", typeof(NzbDrone.Core.Network.GeoIp.GeoIpUpdateCommand))]
    public void ExecuteTask_KnownTasks_PushesStronglyTypedCommand(string typeName, Type expectedCommandType)
    {
        this.taskManager.Get(1).Returns(new ScheduledTask
        {
            Id = 1,
            TypeName = typeName,
        });

        var controller = new SystemTaskController(this.commandQueueManager, this.scheduledTaskRepository, this.taskManager);
        var actionResult = controller.ExecuteTask(1);

        actionResult.Should().BeOfType<OkObjectResult>();
        this.commandQueueManager.Received(1).Push(
            Arg.Is<Command>(c => c.GetType() == expectedCommandType),
            CommandTrigger.Manual);
    }

    [Test]
    public void ExecuteTask_UnknownTask_PushesRawCommand()
    {
        this.taskManager.Get(99).Returns(new ScheduledTask
        {
            Id = 99,
            TypeName = "CustomUnknownTask",
        });

        var controller = new SystemTaskController(this.commandQueueManager, this.scheduledTaskRepository, this.taskManager);
        var actionResult = controller.ExecuteTask(99);

        actionResult.Should().BeOfType<OkObjectResult>();
        this.commandQueueManager.Received(1).PushRaw("CustomUnknown", "{}", CommandTrigger.Manual);
    }
}
