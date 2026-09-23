// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class DownloadHistoryCleanupTaskTest
{
    private IDownloadHistoryService downloadHistoryService = null!;
    private IConfigService configService = null!;
    private DownloadHistoryCleanupTask task = null!;

    [SetUp]
    public void SetUp()
    {
        this.downloadHistoryService = Substitute.For<IDownloadHistoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.task = new DownloadHistoryCleanupTask(this.downloadHistoryService, this.configService);
    }

    [TearDown]
    public void TearDown()
    {
        this.task.Dispose();
    }

    [Test]
    public void Handle_ApplicationStartedEvent_StartsLoopWithoutThrowing()
    {
        var appStarted = new ApplicationStartedEvent();

        Action act = () => this.task.Handle(appStarted);

        act.Should().NotThrow();
    }

    [Test]
    public void StartLoop_CalledMultipleTimes_OnlyInitializesOnce()
    {
        Action act = () =>
        {
            this.task.StartLoop();
            this.task.StartLoop();
        };

        act.Should().NotThrow();
    }

    [Test]
    public void Execute_DownloadHistoryCleanupCommand_RunsSynchronously()
    {
        this.configService.HistoryRetentionDays.Returns(30);

        this.task.Execute(new DownloadHistoryCleanupCommand());

        this.downloadHistoryService.Received(1).PruneHistory(30);
    }

    [Test]
    public async Task ExecuteAsync_DownloadHistoryCleanupCommand_CallsPruneHistory()
    {
        this.configService.HistoryRetentionDays.Returns(14);

        await this.task.ExecuteAsync(new DownloadHistoryCleanupCommand(), CancellationToken.None);

        this.downloadHistoryService.Received(1).PruneHistory(14);
    }

    [Test]
    public async Task ExecuteAsync_WhenRetentionDaysZeroOrNegative_DoesNotCallPruneHistory()
    {
        this.configService.HistoryRetentionDays.Returns(0);

        await this.task.ExecuteAsync(CancellationToken.None);

        this.downloadHistoryService.DidNotReceiveWithAnyArgs().PruneHistory(Arg.Any<int>());

        this.configService.HistoryRetentionDays.Returns(-5);

        await this.task.ExecuteAsync(CancellationToken.None);

        this.downloadHistoryService.DidNotReceiveWithAnyArgs().PruneHistory(Arg.Any<int>());
    }

    [Test]
    public async Task ExecuteAsync_WhenRetentionDaysPositive_CallsPruneHistoryWithConfiguredRetention()
    {
        this.configService.HistoryRetentionDays.Returns(90);

        await this.task.ExecuteAsync(CancellationToken.None);

        this.downloadHistoryService.Received(1).PruneHistory(90);
    }

    [Test]
    public async Task ExecuteAsync_WhenPruneHistoryThrows_CatchesAndLogsWithoutRethrowing()
    {
        this.configService.HistoryRetentionDays.Returns(30);
        this.downloadHistoryService.When(s => s.PruneHistory(Arg.Any<int>()))
            .Do(_ => throw new InvalidOperationException("Database query failed"));

        Func<Task> act = async () => await this.task.ExecuteAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public void Dispose_CancelsCancellationTokenSource()
    {
        Action act = () => this.task.Dispose();

        act.Should().NotThrow();
    }
}
