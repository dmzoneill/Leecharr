// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;

namespace Leecharr.Core.Test.Jobs;

[TestFixture]
public class TaskManagerTest
{
    private IScheduledTaskRepository scheduledTaskRepository = null!;
    private TaskManager taskManager = null!;

    [SetUp]
    public void SetUp()
    {
        this.scheduledTaskRepository = Substitute.For<IScheduledTaskRepository>();
        this.taskManager = new TaskManager(this.scheduledTaskRepository);
    }

    [Test]
    public void GetAll_WhenDatabaseEmpty_SeedsAllDefaultTasksAndReturnsThem()
    {
        var storedTasks = new List<ScheduledTask>();
        this.scheduledTaskRepository.All().Returns(_ => storedTasks.ToList());
        this.scheduledTaskRepository.Insert(Arg.Do<ScheduledTask>(t => storedTasks.Add(t)));

        var result = this.taskManager.GetAll();

        result.Should().HaveCount(9);
        result.Select(t => t.TypeName).Should().BeEquivalentTo(new[]
        {
            "WatchFolderScanTask",
            "RssSyncTask",
            "VpnKillSwitchCheckTask",
            "BackupTask",
            "ProwlarrSyncTask",
            "SessionCleanupTask",
            "BlocklistUpdateTask",
            "GeoIpUpdateTask",
            "DownloadHistoryCleanupTask",
        });

        var watchFolder = result.First(t => t.TypeName == "WatchFolderScanTask");
        watchFolder.Interval.Should().Be(1);

        var rssSync = result.First(t => t.TypeName == "RssSyncTask");
        rssSync.Interval.Should().Be(15);

        var vpnKillSwitch = result.First(t => t.TypeName == "VpnKillSwitchCheckTask");
        vpnKillSwitch.Interval.Should().Be(1);

        var backup = result.First(t => t.TypeName == "BackupTask");
        backup.Interval.Should().Be(1440);

        var prowlarr = result.First(t => t.TypeName == "ProwlarrSyncTask");
        prowlarr.Interval.Should().Be(60);

        var sessionCleanup = result.First(t => t.TypeName == "SessionCleanupTask");
        sessionCleanup.Interval.Should().Be(15);

        var blocklist = result.First(t => t.TypeName == "BlocklistUpdateTask");
        blocklist.Interval.Should().Be(1440);

        var geoIp = result.First(t => t.TypeName == "GeoIpUpdateTask");
        geoIp.Interval.Should().Be(43200);

        var downloadHistory = result.First(t => t.TypeName == "DownloadHistoryCleanupTask");
        downloadHistory.Interval.Should().Be(1440);
    }

    [Test]
    public void GetAll_WhenSomeTasksExist_OnlySeedsMissingTasksAndPreservesCustomizations()
    {
        var existingCustomTask = new ScheduledTask
        {
            Id = 1,
            TypeName = "WatchFolderScanTask",
            Interval = 5,
            LastExecution = DateTime.UtcNow.AddMinutes(-2),
        };

        var storedTasks = new List<ScheduledTask> { existingCustomTask };
        this.scheduledTaskRepository.All().Returns(_ => storedTasks.ToList());
        this.scheduledTaskRepository.Insert(Arg.Do<ScheduledTask>(t => storedTasks.Add(t)));

        var result = this.taskManager.GetAll();

        result.Should().HaveCount(9);
        var watchFolder = result.First(t => t.TypeName == "WatchFolderScanTask");
        watchFolder.Interval.Should().Be(5); // Preserved user customization

        this.scheduledTaskRepository.Received(8).Insert(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void GetAll_WhenAllTasksExist_DoesNotInsertAny()
    {
        var storedTasks = new List<ScheduledTask>
        {
            new() { Id = 1, TypeName = "WatchFolderScanTask", Interval = 1 },
            new() { Id = 2, TypeName = "RssSyncTask", Interval = 15 },
            new() { Id = 3, TypeName = "VpnKillSwitchCheckTask", Interval = 1 },
            new() { Id = 4, TypeName = "BackupTask", Interval = 1440 },
            new() { Id = 5, TypeName = "ProwlarrSyncTask", Interval = 60 },
            new() { Id = 6, TypeName = "SessionCleanupTask", Interval = 15 },
            new() { Id = 7, TypeName = "BlocklistUpdateTask", Interval = 1440 },
            new() { Id = 8, TypeName = "GeoIpUpdateTask", Interval = 43200 },
            new() { Id = 9, TypeName = "DownloadHistoryCleanupTask", Interval = 1440 },
        };

        this.scheduledTaskRepository.All().Returns(storedTasks);

        var result = this.taskManager.GetAll();

        result.Should().HaveCount(9);
        this.scheduledTaskRepository.DidNotReceive().Insert(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void GetByTypeName_WhenMissing_SeedsDefaultTask()
    {
        this.scheduledTaskRepository.GetByTypeName("BackupTask").Returns((ScheduledTask)null!);
        this.scheduledTaskRepository.Insert(Arg.Any<ScheduledTask>()).Returns(callInfo => callInfo.Arg<ScheduledTask>());

        var result = this.taskManager.GetByTypeName("BackupTask");

        result.Should().NotBeNull();
        result.TypeName.Should().Be("BackupTask");
        result.Interval.Should().Be(1440);
        this.scheduledTaskRepository.Received(1).Insert(Arg.Is<ScheduledTask>(t => t.TypeName == "BackupTask" && t.Interval == 1440));
    }

    [Test]
    public void GetByTypeName_WhenExists_ReturnsExistingTaskWithoutSeeding()
    {
        var existing = new ScheduledTask { Id = 42, TypeName = "BackupTask", Interval = 720 };
        this.scheduledTaskRepository.GetByTypeName("BackupTask").Returns(existing);

        var result = this.taskManager.GetByTypeName("BackupTask");

        result.Should().BeSameAs(existing);
        result.Interval.Should().Be(720);
        this.scheduledTaskRepository.DidNotReceive().Insert(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void Handle_ApplicationStartedEvent_EnsuresDefaultTasks()
    {
        var storedTasks = new List<ScheduledTask>();
        this.scheduledTaskRepository.All().Returns(_ => storedTasks.ToList());
        this.scheduledTaskRepository.Insert(Arg.Do<ScheduledTask>(t => storedTasks.Add(t)));

        this.taskManager.Handle(new ApplicationStartedEvent());

        storedTasks.Should().HaveCount(9);
        this.scheduledTaskRepository.Received(9).Insert(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void Update_DelegatesToRepository()
    {
        var task = new ScheduledTask { Id = 5, TypeName = "BackupTask", Interval = 120 };

        this.taskManager.Update(task);

        this.scheduledTaskRepository.Received(1).Update(task);
    }
}
