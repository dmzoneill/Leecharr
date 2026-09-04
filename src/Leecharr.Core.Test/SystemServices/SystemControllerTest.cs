// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Microsoft.AspNetCore.Mvc;
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
        status.AppDataFolder.Should().Be(this.testTempDir);
        status.AppDataPath.Should().Be(this.testTempDir);
        status.StartupPath.Should().Be(this.testStartupDir);
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
}
