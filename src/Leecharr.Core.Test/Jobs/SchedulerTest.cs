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
    public async Task ExecuteAsync_WhenTasksAreDue_DispatchesCommandWithoutUpdatingLastExecution()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "WatchFolderScanTask",
            Interval = 1,
            LastExecution = DateTime.MinValue,
        };

        this.taskManager.GetAll().Returns(new List<ScheduledTask> { task });
        this.commandQueueManager.GetQueued().Returns(new List<CommandModel>());
        this.commandQueueManager.GetStarted().Returns(new List<CommandModel>());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);

        await this.scheduler.StartAsync(cts.Token);
        await Task.Delay(100);
        await this.scheduler.StopAsync(CancellationToken.None);

        this.commandQueueManager.Received().Push(Arg.Any<WatchFolderScanCommand>(), CommandTrigger.Scheduled);
        task.LastStartTime.Should().NotBeNull();
        task.LastExecution.Should().Be(DateTime.MinValue);
    }

    [Test]
    public async Task ExecuteAsync_WhenTaskIsAlreadyQueued_SkipsDuplicateDispatch()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "WatchFolderScanTask",
            Interval = 1,
            LastExecution = DateTime.MinValue,
        };

        var queuedCommand = new CommandModel
        {
            Id = 10,
            Name = "WatchFolderScanCommand",
            Status = CommandStatus.Queued,
        };

        this.taskManager.GetAll().Returns(new List<ScheduledTask> { task });
        this.commandQueueManager.GetQueued().Returns(new List<CommandModel> { queuedCommand });
        this.commandQueueManager.GetStarted().Returns(new List<CommandModel>());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);

        await this.scheduler.StartAsync(cts.Token);
        await Task.Delay(100);
        await this.scheduler.StopAsync(CancellationToken.None);

        this.commandQueueManager.DidNotReceive().Push(Arg.Any<WatchFolderScanCommand>(), Arg.Any<CommandTrigger>());
    }

    [Test]
    public async Task ExecuteAsync_WhenTaskIsAlreadyRunning_SkipsDuplicateDispatch()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "WatchFolderScanTask",
            Interval = 1,
            LastExecution = DateTime.MinValue,
        };

        var runningCommand = new CommandModel
        {
            Id = 10,
            Name = "WatchFolderScan",
            Status = CommandStatus.Running,
        };

        this.taskManager.GetAll().Returns(new List<ScheduledTask> { task });
        this.commandQueueManager.GetQueued().Returns(new List<CommandModel>());
        this.commandQueueManager.GetStarted().Returns(new List<CommandModel> { runningCommand });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);

        await this.scheduler.StartAsync(cts.Token);
        await Task.Delay(100);
        await this.scheduler.StopAsync(CancellationToken.None);

        this.commandQueueManager.DidNotReceive().Push(Arg.Any<WatchFolderScanCommand>(), Arg.Any<CommandTrigger>());
    }

    [Test]
    public void Handle_CommandExecutedEvent_UpdatesTaskLastExecutionAndLastStartTime()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "WatchFolderScanTask",
            Interval = 1,
            LastExecution = DateTime.MinValue,
            LastStartTime = null,
        };

        this.taskManager.GetAll().Returns(new List<ScheduledTask> { task });

        var startedAt = DateTime.UtcNow.AddSeconds(-10);
        var endedAt = DateTime.UtcNow;

        var command = new CommandModel
        {
            Id = 5,
            Name = "WatchFolderScanCommand",
            Status = CommandStatus.Completed,
            StartedAt = startedAt,
            EndedAt = endedAt,
        };

        this.scheduler.Handle(new CommandExecutedEvent(command));

        task.LastExecution.Should().Be(endedAt);
        task.LastStartTime.Should().Be(startedAt);
        this.taskManager.Received(1).Update(task);
    }

    [Test]
    public async Task ExecuteAsync_WhenCommandCompletedInQueueHistory_SyncsLastExecution()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "WatchFolderScanTask",
            Interval = 1,
            LastExecution = DateTime.MinValue,
        };

        var startedAt = DateTime.UtcNow.AddMinutes(-1);
        var endedAt = DateTime.UtcNow.AddSeconds(-10);

        var completedCommand = new CommandModel
        {
            Id = 15,
            Name = "WatchFolderScanCommand",
            Status = CommandStatus.Completed,
            StartedAt = startedAt,
            EndedAt = endedAt,
        };

        this.taskManager.GetAll().Returns(new List<ScheduledTask> { task });
        this.commandQueueManager.GetAll().Returns(new List<CommandModel> { completedCommand });
        this.commandQueueManager.GetQueued().Returns(new List<CommandModel>());
        this.commandQueueManager.GetStarted().Returns(new List<CommandModel>());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);

        await this.scheduler.StartAsync(cts.Token);
        await Task.Delay(100);
        await this.scheduler.StopAsync(CancellationToken.None);

        task.LastExecution.Should().Be(endedAt);
        task.LastStartTime.Should().Be(startedAt);
        this.taskManager.Received().Update(task);
    }
}
