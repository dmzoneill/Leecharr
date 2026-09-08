// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
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
        model.Name.Should().Be("SampleTest");
        model.Status.Should().Be(CommandStatus.Queued);
        model.Trigger.Should().Be((int)CommandTrigger.Manual);
        this.repository.Received(1).Insert(Arg.Is<CommandModel>(m => m.Name == "SampleTest" && m.Status == CommandStatus.Queued));
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
}
