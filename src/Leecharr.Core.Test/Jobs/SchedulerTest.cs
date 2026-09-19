// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.WatchFolder;

namespace Leecharr.Core.Test.Jobs;

[TestFixture]
public class SchedulerTest
{
    private ITaskManager taskManager = null!;
    private IManageCommandQueue commandQueueManager = null!;
    private Scheduler scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        this.taskManager = Substitute.For<ITaskManager>();
        this.commandQueueManager = Substitute.For<IManageCommandQueue>();
        this.scheduler = new Scheduler(this.taskManager, this.commandQueueManager);
    }

    [Test]
    public async Task ExecuteAsync_WhenTasksAreDue_DispatchesCommands()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "WatchFolderScanTask",
            Interval = 1,
            LastExecution = DateTime.MinValue,
        };

        this.taskManager.GetAll().Returns(new List<ScheduledTask> { task });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);

        await this.scheduler.StartAsync(cts.Token);
        await Task.Delay(100);
        await this.scheduler.StopAsync(CancellationToken.None);

        this.commandQueueManager.Received().Push(Arg.Any<WatchFolderScanCommand>(), CommandTrigger.Scheduled);
    }
}
