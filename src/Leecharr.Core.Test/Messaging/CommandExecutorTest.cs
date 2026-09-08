// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
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

public class SampleAsyncTestCommand : Command
{
    public string Payload { get; set; } = string.Empty;
}

public class SampleAsyncTestCommandHandler : IExecuteAsync<SampleAsyncTestCommand>
{
    public bool Executed { get; private set; }

    public CancellationToken ReceivedCancellationToken { get; private set; }

    public async Task ExecuteAsync(SampleAsyncTestCommand message, CancellationToken cancellationToken = default)
    {
        this.ReceivedCancellationToken = cancellationToken;
        await Task.Yield();
        this.Executed = true;
    }
}

public class SampleCancellingAsyncCommandHandler : IExecuteAsync<SampleAsyncTestCommand>
{
    public async Task ExecuteAsync(SampleAsyncTestCommand message, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1000, cancellationToken);
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

    [Test]
    public async Task ExecuteAsync_ExecutesAsyncHandlerAndPropagatesCancellationToken()
    {
        var commandModel = new CommandModel
        {
            Id = 3,
            Name = "SampleAsyncTest",
            Status = CommandStatus.Queued,
            Body = "{}",
        };

        var handler = new SampleAsyncTestCommandHandler();
        this.serviceFactory.Build(typeof(IExecuteAsync<SampleAsyncTestCommand>)).Returns(handler);

        using var cts = new System.Threading.CancellationTokenSource();
        await this.executor.ExecuteAsync(commandModel, cts.Token);

        handler.Executed.Should().BeTrue();
        handler.ReceivedCancellationToken.Should().Be(cts.Token);
        commandModel.Status.Should().Be(CommandStatus.Completed);
    }

    [Test]
    public async Task ExecuteAsync_SetsFailedStatusWhenCancelled()
    {
        var commandModel = new CommandModel
        {
            Id = 4,
            Name = "SampleAsyncTest",
            Status = CommandStatus.Queued,
            Body = "{}",
        };

        var handler = new SampleCancellingAsyncCommandHandler();
        this.serviceFactory.Build(typeof(IExecuteAsync<SampleAsyncTestCommand>)).Returns(handler);

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await this.executor.ExecuteAsync(commandModel, cts.Token);

        commandModel.Status.Should().Be(CommandStatus.Failed);
        commandModel.Message.Should().Be("Command execution cancelled.");
    }

    [Test]
    public async Task CommandWorker_DequeuesAndExecutesQueuedCommands()
    {
        var queue = Substitute.For<IManageCommandQueue>();
        var commandModel = new CommandModel
        {
            Id = 5,
            Name = "SampleTest",
            Status = CommandStatus.Queued,
            Body = "{}",
        };

        var queued = new System.Collections.Generic.List<CommandModel> { commandModel };
        queue.GetQueued().Returns(_ =>
        {
            var result = queued.ToArray();
            queued.Clear();
            return result;
        });

        var commandExecutor = Substitute.For<ICommandExecutor>();
        var worker = new CommandWorker(queue, commandExecutor);

        using var cts = new System.Threading.CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();
        await worker.StopAsync(System.Threading.CancellationToken.None);

        await commandExecutor.Received(1).ExecuteAsync(commandModel, Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public async Task CommandWorker_CallsFailStaleCommandsOnStartup()
    {
        var queue = Substitute.For<IManageCommandQueue>();
        queue.GetQueued().Returns(System.Array.Empty<CommandModel>());

        var commandExecutor = Substitute.For<ICommandExecutor>();
        var worker = new CommandWorker(queue, commandExecutor);

        using var cts = new System.Threading.CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        await Task.Delay(50);
        cts.Cancel();
        await worker.StopAsync(System.Threading.CancellationToken.None);

        queue.Received(1).FailStaleCommands();
    }

    [Test]
    [TestCase("WatchFolderScan")]
    [TestCase("WatchFolderScanCommand")]
    [TestCase("watchfolderscan")]
    [TestCase("RssSync")]
    [TestCase("RssSyncCommand")]
    [TestCase("rsssync")]
    [TestCase("VpnKillSwitchCheck")]
    [TestCase("VpnKillSwitchCheckCommand")]
    [TestCase("vpnkillswitchcheck")]
    [TestCase("ProwlarrSync")]
    [TestCase("ProwlarrSyncCommand")]
    [TestCase("prowlarrsync")]
    [TestCase("Backup")]
    [TestCase("BackupCommand")]
    [TestCase("backup")]
    public async Task ExecuteAsync_ResolvesAndExecutesSystemTaskCommands(string commandName)
    {
        var commandModel = new CommandModel
        {
            Id = 10,
            Name = commandName,
            Status = CommandStatus.Queued,
            Body = "{}",
        };

        var executeAsyncCalled = false;

        this.serviceFactory.Build(Arg.Any<Type>()).Returns(callInfo =>
        {
            var requestedType = callInfo.Arg<Type>();
            if (requestedType == typeof(IExecuteAsync<NzbDrone.Core.WatchFolder.WatchFolderScanCommand>))
            {
                var h = Substitute.For<IExecuteAsync<NzbDrone.Core.WatchFolder.WatchFolderScanCommand>>();
                h.ExecuteAsync(Arg.Any<NzbDrone.Core.WatchFolder.WatchFolderScanCommand>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        executeAsyncCalled = true;
                        return Task.CompletedTask;
                    });
                return h;
            }

            if (requestedType == typeof(IExecuteAsync<NzbDrone.Core.Indexers.RssSyncCommand>))
            {
                var h = Substitute.For<IExecuteAsync<NzbDrone.Core.Indexers.RssSyncCommand>>();
                h.ExecuteAsync(Arg.Any<NzbDrone.Core.Indexers.RssSyncCommand>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        executeAsyncCalled = true;
                        return Task.CompletedTask;
                    });
                return h;
            }

            if (requestedType == typeof(IExecuteAsync<NzbDrone.Core.Network.VpnKillSwitchCheckCommand>))
            {
                var h = Substitute.For<IExecuteAsync<NzbDrone.Core.Network.VpnKillSwitchCheckCommand>>();
                h.ExecuteAsync(Arg.Any<NzbDrone.Core.Network.VpnKillSwitchCheckCommand>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        executeAsyncCalled = true;
                        return Task.CompletedTask;
                    });
                return h;
            }

            if (requestedType == typeof(IExecuteAsync<NzbDrone.Core.Indexers.ProwlarrSyncCommand>))
            {
                var h = Substitute.For<IExecuteAsync<NzbDrone.Core.Indexers.ProwlarrSyncCommand>>();
                h.ExecuteAsync(Arg.Any<NzbDrone.Core.Indexers.ProwlarrSyncCommand>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        executeAsyncCalled = true;
                        return Task.CompletedTask;
                    });
                return h;
            }

            if (requestedType == typeof(IExecuteAsync<NzbDrone.Core.Backup.BackupCommand>))
            {
                var h = Substitute.For<IExecuteAsync<NzbDrone.Core.Backup.BackupCommand>>();
                h.ExecuteAsync(Arg.Any<NzbDrone.Core.Backup.BackupCommand>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        executeAsyncCalled = true;
                        return Task.CompletedTask;
                    });
                return h;
            }

            return null;
        });

        await this.executor.ExecuteAsync(commandModel);

        executeAsyncCalled.Should().BeTrue();
        commandModel.Status.Should().Be(CommandStatus.Completed);
    }
}
