// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.WatchFolder;

namespace Leecharr.Core.Test.Messaging;

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
    public void Execute_WhenCommandIsNull_DoesNothing()
    {
        this.executor.Execute(null!);
        this.repository.DidNotReceive().Update(Arg.Any<CommandModel>());
    }

    [Test]
    public void Execute_WhenCommandTypeUnknown_FailsAndUpdatesRepository()
    {
        var model = new CommandModel
        {
            Id = 1,
            Name = "NonExistentCommandXYZ",
            Status = CommandStatus.Queued,
        };

        this.executor.Execute(model);

        model.Status.Should().Be(CommandStatus.Failed);
        model.Message.Should().Contain("Unknown command");
        model.EndedAt.Should().NotBeNull();
        this.repository.Received().Update(model);
    }

    [Test]
    public void Execute_WhenHandlerNotRegistered_FailsAndUpdatesRepository()
    {
        var model = new CommandModel
        {
            Id = 2,
            Name = "SessionCleanupCommand",
            Status = CommandStatus.Queued,
        };

        this.serviceFactory.Build(typeof(IExecute<SessionCleanupCommand>)).Returns(_ => throw new Exception("No registration"));

        this.executor.Execute(model);

        model.Status.Should().Be(CommandStatus.Failed);
        model.Message.Should().Contain("No handler for command");
        model.EndedAt.Should().NotBeNull();
    }

    [Test]
    public void Execute_WhenCommandNameLacksCommandSuffix_FindsAndExecutesHandler()
    {
        var model = new CommandModel
        {
            Id = 3,
            Name = "WatchFolderScan",
            Status = CommandStatus.Queued,
        };

        var handler = Substitute.For<IExecute<WatchFolderScanCommand>>();
        this.serviceFactory.Build(typeof(IExecute<WatchFolderScanCommand>)).Returns(handler);

        this.executor.Execute(model);

        handler.Received(1).Execute(Arg.Any<WatchFolderScanCommand>());
        model.Status.Should().Be(CommandStatus.Completed);
        model.EndedAt.Should().NotBeNull();
    }

    [Test]
    public void Execute_WhenCommandNameHasTaskSuffix_FindsAndExecutesHandler()
    {
        var model = new CommandModel
        {
            Id = 4,
            Name = "RssSyncTask",
            Status = CommandStatus.Queued,
        };

        var handler = Substitute.For<IExecute<RssSyncCommand>>();
        this.serviceFactory.Build(typeof(IExecute<RssSyncCommand>)).Returns(handler);

        this.executor.Execute(model);

        handler.Received(1).Execute(Arg.Any<RssSyncCommand>());
        model.Status.Should().Be(CommandStatus.Completed);
    }

    [Test]
    public void Execute_PersistsRunningStatusBeforeExecution()
    {
        var model = new CommandModel
        {
            Id = 5,
            Name = "VpnKillSwitchCheck",
            Status = CommandStatus.Queued,
        };

        var updates = new List<CommandStatus>();
        this.repository.When(r => r.Update(Arg.Any<CommandModel>())).Do(call =>
        {
            var cmd = call.Arg<CommandModel>();
            updates.Add(cmd.Status);
        });

        var handler = Substitute.For<IExecute<VpnKillSwitchCheckCommand>>();
        this.serviceFactory.Build(typeof(IExecute<VpnKillSwitchCheckCommand>)).Returns(handler);

        this.executor.Execute(model);

        updates.Should().ContainInOrder(CommandStatus.Running, CommandStatus.Completed);
    }

    [Test]
    public void Execute_WhenBackupCommandExecuted_InvokesBackupHandler()
    {
        var model = new CommandModel
        {
            Id = 6,
            Name = "Backup",
            Status = CommandStatus.Queued,
        };

        var handler = Substitute.For<IExecute<BackupCommand>>();
        this.serviceFactory.Build(typeof(IExecute<BackupCommand>)).Returns(handler);

        this.executor.Execute(model);

        handler.Received(1).Execute(Arg.Any<BackupCommand>());
        model.Status.Should().Be(CommandStatus.Completed);
    }
}
