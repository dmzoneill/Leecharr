// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class SystemControllerTest
{
    private IAppFolderInfo appFolderInfo = null!;
    private string testTempDir = null!;
    private string testStartupDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.testTempDir = Path.Combine(Path.GetTempPath(), "LeecharrSystemTest_Data_" + Guid.NewGuid().ToString("N"));
        this.testStartupDir = Path.Combine(Path.GetTempPath(), "LeecharrSystemTest_Start_" + Guid.NewGuid().ToString("N"));

        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.testTempDir);
        this.appFolderInfo.StartUpFolder.Returns(this.testStartupDir);
    }

    [Test]
    public void GetStatus_ReturnsStatusWithPopulatedFields()
    {
        var controller = new SystemController(this.appFolderInfo);

        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var status = okResult!.Value as SystemStatusResource;
        status.Should().NotBeNull();
        status!.AppName.Should().Be("Leecharr");
        status.RuntimeName.Should().Be(".NET");
        status.AppDataFolder.Should().Be(SystemController.SanitizeHostPath(this.testTempDir));
        status.AppDataPath.Should().Be(SystemController.SanitizeHostPath(this.testTempDir));
        status.StartupPath.Should().Be(SystemController.SanitizeHostPath(this.testStartupDir));
        status.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
        status.DatabaseMigration.Should().Be("18");
        status.DatabaseType.Should().Be("SQLite");
        status.DatabaseVersion.Should().Be("SQLite");
    }

    [Test]
    public void SystemStatusResource_AppDataPath_SynchronizesWithAppDataFolder()
    {
        var resource = new SystemStatusResource
        {
            AppDataFolder = "/initial/path",
        };

        resource.AppDataPath.Should().Be("/initial/path");

        resource.AppDataPath = "/new/path";
        resource.AppDataFolder.Should().Be("/new/path");
    }

    [Test]
    public void SystemStatusResource_UptimeSeconds_CalculatesFromStartTime()
    {
        var resource = new SystemStatusResource
        {
            StartTime = DateTime.UtcNow.AddSeconds(-300),
        };

        resource.UptimeSeconds.Should().BeInRange(299, 310);
    }

    [Test]
    public void GetStatus_WithDatabase_ReturnsConfiguredDatabaseType()
    {
        var database = Substitute.For<IDatabase>();
        database.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var controller = new SystemController(this.appFolderInfo, database);

        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var status = okResult!.Value as SystemStatusResource;
        status.Should().NotBeNull();
        status!.DatabaseType.Should().Be("PostgreSQL");
        status.DatabaseVersion.Should().Be("PostgreSQL");
    }

    [Test]
    [TestCase("/home/johndoe/.config/Leecharr", "~/.config/Leecharr")]
    [TestCase("/Users/johndoe/Library/Application Support/Leecharr", "~/Library/Application Support/Leecharr")]
    [TestCase(@"C:\Users\johndoe\AppData\Roaming\Leecharr", @"~\AppData\Roaming\Leecharr")]
    public void SanitizeHostPath_RedactsUserHomeDirectories(string input, string expected)
    {
        SystemController.SanitizeHostPath(input).Should().Be(expected);
    }

    [Test]
    public void GetStatus_WhenAppDataInUserHome_RedactsHomeDirectory()
    {
        this.appFolderInfo.AppDataFolder.Returns("/home/someuser/.config/Leecharr");
        this.appFolderInfo.StartUpFolder.Returns("/home/someuser/bin/Leecharr");

        var controller = new SystemController(this.appFolderInfo);
        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var status = okResult!.Value as SystemStatusResource;
        status.Should().NotBeNull();
        status!.AppDataFolder.Should().Be("~/.config/Leecharr");
        status.StartupPath.Should().Be("~/bin/Leecharr");
    }

    [Test]
    public async Task Restart_OnSystemRestartController_SetsRestartPendingAndStopsApplication()
    {
        var runtimeInfo = Substitute.For<IRuntimeInfo>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var controller = new SystemRestartController(runtimeInfo, lifetime);

        var result = controller.Restart();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        runtimeInfo.RestartPending.Should().BeTrue();

        await Task.Delay(600);
        lifetime.Received(1).StopApplication();
    }

    [Test]
    public async Task Shutdown_OnSystemShutdownController_SetsRestartPendingFalseAndStopsApplication()
    {
        var runtimeInfo = Substitute.For<IRuntimeInfo>();
        runtimeInfo.RestartPending = true;
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var controller = new SystemShutdownController(runtimeInfo, lifetime);

        var result = controller.Shutdown();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        runtimeInfo.RestartPending.Should().BeFalse();

        await Task.Delay(600);
        lifetime.Received(1).StopApplication();
    }

    [Test]
    public async Task Restart_OnSystemController_SetsRestartPendingAndStopsApplication()
    {
        var runtimeInfo = Substitute.For<IRuntimeInfo>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var controller = new SystemController(this.appFolderInfo, runtimeInfo: runtimeInfo, hostApplicationLifetime: lifetime);

        var result = controller.Restart();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        runtimeInfo.RestartPending.Should().BeTrue();

        await Task.Delay(600);
        lifetime.Received(1).StopApplication();
    }

    [Test]
    public async Task Shutdown_OnSystemController_SetsRestartPendingFalseAndStopsApplication()
    {
        var runtimeInfo = Substitute.For<IRuntimeInfo>();
        runtimeInfo.RestartPending = true;
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var controller = new SystemController(this.appFolderInfo, runtimeInfo: runtimeInfo, hostApplicationLifetime: lifetime);

        var result = controller.Shutdown();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        runtimeInfo.RestartPending.Should().BeFalse();

        await Task.Delay(600);
        lifetime.Received(1).StopApplication();
    }

    [Test]
    public void RuntimeInfo_RestartPending_SynchronizesAcrossInstances()
    {
        var runtime1 = new RuntimeInfo();
        var runtime2 = new RuntimeInfo();

        runtime1.RestartPending = false;
        runtime2.RestartPending.Should().BeFalse();

        runtime1.RestartPending = true;
        runtime2.RestartPending.Should().BeTrue();

        runtime2.RestartPending = false;
        runtime1.RestartPending.Should().BeFalse();
    }
}
