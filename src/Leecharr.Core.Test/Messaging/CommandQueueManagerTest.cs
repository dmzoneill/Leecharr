// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;

namespace Leecharr.Core.Test.Messaging;

[TestFixture]
public class CommandQueueManagerTest
{
    private ICommandRepository repository = null!;
    private CommandQueueManager commandQueueManager = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<ICommandRepository>();
        this.commandQueueManager = new CommandQueueManager(this.repository);
    }

    [TearDown]
    public void TearDown()
    {
        this.commandQueueManager.Dispose();
    }

    [Test]
    public void FailStaleCommands_WhenRunningCommandsExist_MarksAllAsFailedWithInterruptedMessageAndEndedAt()
    {
        var runningCommand1 = new CommandModel
        {
            Id = 101,
            Name = "ProwlarrSync",
            Status = CommandStatus.Running,
            QueuedAt = DateTime.UtcNow.AddMinutes(-30),
            StartedAt = DateTime.UtcNow.AddMinutes(-29),
        };

        var runningCommand2 = new CommandModel
        {
            Id = 102,
            Name = "RssSync",
            Status = CommandStatus.Running,
            QueuedAt = DateTime.UtcNow.AddMinutes(-10),
            StartedAt = DateTime.UtcNow.AddMinutes(-9),
        };

        this.repository.GetByStatus(CommandStatus.Running)
            .Returns(new List<CommandModel> { runningCommand1, runningCommand2 });

        var updatedCommands = new List<CommandModel>();
        this.repository.When(r => r.Update(Arg.Any<CommandModel>()))
            .Do(callInfo => updatedCommands.Add(callInfo.Arg<CommandModel>()));

        var beforeSweep = DateTime.UtcNow;

        this.commandQueueManager.FailStaleCommands();

        var afterSweep = DateTime.UtcNow;

        updatedCommands.Should().HaveCount(2);

        runningCommand1.Status.Should().Be(CommandStatus.Failed);
        runningCommand1.Message.Should().Be("Command was interrupted by application restart.");
        runningCommand1.EndedAt.Should().NotBeNull();
        runningCommand1.EndedAt.Value.Should().BeOnOrAfter(beforeSweep).And.BeOnOrBefore(afterSweep);

        runningCommand2.Status.Should().Be(CommandStatus.Failed);
        runningCommand2.Message.Should().Be("Command was interrupted by application restart.");
        runningCommand2.EndedAt.Should().NotBeNull();
        runningCommand2.EndedAt.Value.Should().BeOnOrAfter(beforeSweep).And.BeOnOrBefore(afterSweep);

        this.repository.Received(2).Update(Arg.Any<CommandModel>());
    }

    [Test]
    public void FailStaleCommands_WhenNoRunningCommands_DoesNotUpdateRepository()
    {
        this.repository.GetByStatus(CommandStatus.Running)
            .Returns(new List<CommandModel>());

        this.commandQueueManager.FailStaleCommands();

        this.repository.DidNotReceive().Update(Arg.Any<CommandModel>());
    }

    [Test]
    public void Push_CreatesQueuedCommandModelAndInsertsIntoRepository()
    {
        var command = new SampleTestCommand { Payload = "test" };

        var model = this.commandQueueManager.Push(command, CommandTrigger.Manual);

        model.Should().NotBeNull();
        model.Name.Should().Be("SampleTestCommand");
        model.Status.Should().Be(CommandStatus.Queued);
        model.Trigger.Should().Be((int)CommandTrigger.Manual);
        this.repository.Received(1).Insert(Arg.Is<CommandModel>(m => m.Name == "SampleTestCommand" && m.Status == CommandStatus.Queued));
    }

    [Test]
    public void Push_ScheduledTrigger_IsSerializedInStoredBody()
    {
        var command = new SampleTestCommand { Payload = "blocklist" };
        var inserted = new List<CommandModel>();
        this.repository.When(r => r.Insert(Arg.Any<CommandModel>()))
            .Do(callInfo => inserted.Add(callInfo.Arg<CommandModel>()));

        this.commandQueueManager.Push(command, CommandTrigger.Scheduled);

        inserted.Should().ContainSingle();
        inserted[0].Body.Should().Contain("\"trigger\":\"scheduled\"");
        inserted[0].Trigger.Should().Be((int)CommandTrigger.Scheduled);
    }

    [Test]
    public void Push_WhenMatchingCommandAlreadyQueued_ReturnsExistingModelWithoutInserting()
    {
        var command = new SampleTestCommand { Payload = "test" };
        var existing = new CommandModel
        {
            Id = 42,
            Name = "SampleTestCommand",
            Status = CommandStatus.Queued,
        };

        this.repository.FindExisting("SampleTestCommand", Arg.Any<string>()).Returns(existing);

        var result = this.commandQueueManager.Push(command, CommandTrigger.Manual);

        result.Should().BeSameAs(existing);
        this.repository.DidNotReceive().Insert(Arg.Any<CommandModel>());
    }

    [Test]
    public void Push_WhenMatchingCommandAlreadyRunning_ReturnsExistingModelWithoutInserting()
    {
        var command = new SampleTestCommand { Payload = "test" };
        var existing = new CommandModel
        {
            Id = 43,
            Name = "SampleTestCommand",
            Status = CommandStatus.Running,
        };

        this.repository.FindExisting("SampleTestCommand", Arg.Any<string>()).Returns(existing);

        var result = this.commandQueueManager.Push(command, CommandTrigger.Scheduled);

        result.Should().BeSameAs(existing);
        this.repository.DidNotReceive().Insert(Arg.Any<CommandModel>());
    }

    [Test]
    public void Push_WhenNoExistingMatchingCommand_InsertsAndReturnsNewModel()
    {
        var command = new SampleTestCommand { Payload = "distinct-payload" };
        this.repository.FindExisting("SampleTestCommand", Arg.Any<string>()).Returns((CommandModel)null!);

        var result = this.commandQueueManager.Push(command, CommandTrigger.Manual);

        result.Should().NotBeNull();
        result.Name.Should().Be("SampleTestCommand");
        result.Status.Should().Be(CommandStatus.Queued);
        this.repository.Received(1).Insert(Arg.Is<CommandModel>(m => m.Name == "SampleTestCommand" && m.Status == CommandStatus.Queued));
    }

    [Test]
    public void PushRaw_WhenMatchingCommandAlreadyPending_ReturnsExistingModelWithoutInserting()
    {
        var existing = new CommandModel
        {
            Id = 44,
            Name = "RssSync",
            Status = CommandStatus.Queued,
        };

        this.repository.FindExisting("RssSync", "{}").Returns(existing);

        var result = this.commandQueueManager.PushRaw("RssSync", "{}", CommandTrigger.Manual);

        result.Should().BeSameAs(existing);
        this.repository.DidNotReceive().Insert(Arg.Any<CommandModel>());
    }

    [Test]
    public void GetStarted_QueriesRepositoryForRunningStatus()
    {
        var running = new List<CommandModel>
        {
            new() { Id = 1, Status = CommandStatus.Running, Name = "RunningCmd" },
        };
        this.repository.GetByStatus(CommandStatus.Running).Returns(running);

        var result = this.commandQueueManager.GetStarted().ToList();

        result.Should().BeEquivalentTo(running);
        this.repository.Received(1).GetByStatus(CommandStatus.Running);
    }

    [Test]
    public void GetQueued_QueriesRepositoryForQueuedStatus()
    {
        var queued = new List<CommandModel>
        {
            new() { Id = 2, Status = CommandStatus.Queued, Name = "QueuedCmd" },
        };
        this.repository.GetByStatus(CommandStatus.Queued).Returns(queued);

        var result = this.commandQueueManager.GetQueued().ToList();

        result.Should().BeEquivalentTo(queued);
        this.repository.Received(1).GetByStatus(CommandStatus.Queued);
    }

    [Test]
    public void Cancel_WhenRunning_SignalsLinkedExecutionCancellation()
    {
        var running = new CommandModel
        {
            Id = 77,
            Name = "Backup",
            Status = CommandStatus.Running,
        };

        this.repository.Get(77).Returns(running);

        using var shutdown = new CancellationTokenSource();
        var executionToken = this.commandQueueManager.LinkExecutionCancellation(77, shutdown.Token);

        var cancelled = false;
        executionToken.Register(() => cancelled = true);

        this.commandQueueManager.Cancel(77).Should().BeTrue();

        cancelled.Should().BeTrue();
        running.Status.Should().Be(CommandStatus.Cancelled);
        this.repository.Received(1).Update(running);
    }
}
