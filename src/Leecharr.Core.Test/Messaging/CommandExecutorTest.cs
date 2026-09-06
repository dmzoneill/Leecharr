// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace Leecharr.Core.Test.Messaging;

public class SampleTestCommand : Command
{
    public string Payload { get; set; } = string.Empty;
}

public class SampleTestCommandHandler : IExecute<SampleTestCommand>
{
    public bool Executed { get; private set; }

    public void Execute(SampleTestCommand message)
    {
        this.Executed = true;
    }
}

[TestFixture]
public class CommandExecutorTest
{
    private IServiceFactory serviceFactory = null!;
    private IBasicRepository<CommandModel> repository = null!;
    private CommandExecutor executor = null!;

    [SetUp]
    public void SetUp()
    {
        this.serviceFactory = Substitute.For<IServiceFactory>();
        this.repository = Substitute.For<IBasicRepository<CommandModel>>();
        this.executor = new CommandExecutor(this.serviceFactory, this.repository);
    }

    [Test]
    public void Execute_PersistsRunningStatusBeforeHandlerExecution()
    {
        var commandModel = new CommandModel
        {
            Id = 1,
            Name = "SampleTest",
            Status = CommandStatus.Queued,
            Body = "{}",
        };

        var handler = new SampleTestCommandHandler();
        this.serviceFactory.Build(typeof(IExecute<SampleTestCommand>)).Returns(handler);

        var statusHistory = new System.Collections.Generic.List<CommandStatus>();
        this.repository.When(r => r.Update(Arg.Any<CommandModel>()))
            .Do(callInfo =>
            {
                var model = callInfo.Arg<CommandModel>();
                statusHistory.Add(model.Status);
            });

        this.executor.Execute(commandModel);

        handler.Executed.Should().BeTrue();
        statusHistory.Should().ContainInOrder(CommandStatus.Running, CommandStatus.Completed);
    }

    [Test]
    public void Execute_FindsCommandWithoutCommandSuffixAndDifferentCasing()
    {
        var commandModel = new CommandModel
        {
            Id = 2,
            Name = "sampletest",
            Status = CommandStatus.Queued,
            Body = "{}",
        };

        var handler = new SampleTestCommandHandler();
        this.serviceFactory.Build(typeof(IExecute<SampleTestCommand>)).Returns(handler);

        this.executor.Execute(commandModel);

        handler.Executed.Should().BeTrue();
        commandModel.Status.Should().Be(CommandStatus.Completed);
    }
}
