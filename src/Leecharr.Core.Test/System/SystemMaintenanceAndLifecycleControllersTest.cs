// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class SystemMaintenanceControllerTest
{
    private IDatabase database = null!;
    private SystemMaintenanceController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.database = Substitute.For<IDatabase>();
        this.controller = new SystemMaintenanceController(this.database);
    }

    [Test]
    public async Task Vacuum_WhenDatabaseExecutesSuccessfully_ReturnsOkWithSuccessResult()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;");
        connection.Open();
        this.database.OpenConnection().Returns(connection);

        var result = await this.controller.Vacuum();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(200);

        var successProp = okResult.Value!.GetType().GetProperty("Success")?.GetValue(okResult.Value);
        successProp.Should().Be(true);

        var messageProp = okResult.Value!.GetType().GetProperty("Message")?.GetValue(okResult.Value);
        messageProp.Should().Be("Database VACUUM completed successfully.");
    }

    [Test]
    public async Task Vacuum_WhenDatabaseThrowsException_Returns500WithErrorMessage()
    {
        this.database.OpenConnection()
            .Throws(new InvalidOperationException("Disk I/O failure while writing to SQLite temporary file."));

        var result = await this.controller.Vacuum();

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(500);

        var successProp = objResult.Value!.GetType().GetProperty("Success")?.GetValue(objResult.Value);
        successProp.Should().Be(false);

        var messageProp = objResult.Value!.GetType().GetProperty("Message")?.GetValue(objResult.Value);
        messageProp.Should().Be("Disk I/O failure while writing to SQLite temporary file.");
    }

    [Test]
    public void SystemMaintenanceController_RequiresAdminAuthorizationPolicy()
    {
        var type = typeof(SystemMaintenanceController);
        var authAttribute = type.GetCustomAttribute<AuthorizeAttribute>();

        authAttribute.Should().NotBeNull();
        authAttribute!.Policy.Should().Be("RequireAdmin");
    }

    [Test]
    public void SystemMaintenanceController_HasV1ApiControllerAttribute()
    {
        var type = typeof(SystemMaintenanceController);
        var apiAttribute = type.GetCustomAttribute<V1ApiControllerAttribute>();

        apiAttribute.Should().NotBeNull();
        apiAttribute!.Resource.Should().Be("system/maintenance");
    }

    [Test]
    public void Vacuum_HasExpectedHttpPostRoutingAttributes()
    {
        var type = typeof(SystemMaintenanceController);
        var method = type.GetMethod(nameof(SystemMaintenanceController.Vacuum));
        method.Should().NotBeNull();

        var httpPostAttributes = method!.GetCustomAttributes<HttpPostAttribute>().ToList();
        httpPostAttributes.Should().HaveCount(2);
        httpPostAttributes.Select(a => a.Template).Should().Contain(new[] { "vacuum", "/api/v1/system/database/vacuum" });
    }
}

[TestFixture]
public class SystemRestartControllerTest
{
    private IRuntimeInfo runtimeInfo = null!;
    private IHostApplicationLifetime hostApplicationLifetime = null!;
    private SystemRestartController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.runtimeInfo = Substitute.For<IRuntimeInfo>();
        this.hostApplicationLifetime = Substitute.For<IHostApplicationLifetime>();
        this.controller = new SystemRestartController(this.runtimeInfo, this.hostApplicationLifetime);
    }

    [Test]
    public void Restart_SetsRestartPendingToTrue_AndReturnsOk()
    {
        this.runtimeInfo.RestartPending = false;

        var result = this.controller.Restart();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(200);

        var messageProp = okResult.Value!.GetType().GetProperty("message")?.GetValue(okResult.Value);
        messageProp.Should().Be("Restarting Leecharr...");

        this.runtimeInfo.RestartPending.Should().BeTrue();
    }

    [Test]
    public async Task Restart_TriggersStopApplicationAfterDelay()
    {
        this.controller.Restart();

        // SystemRestartController delays for 500ms before stopping the application
        await Task.Delay(650);

        this.hostApplicationLifetime.Received(1).StopApplication();
    }

    [Test]
    public void Restart_WhenRuntimeInfoIsNull_DoesNotThrow()
    {
        var standaloneController = new SystemRestartController(null!, this.hostApplicationLifetime);

        var action = () => standaloneController.Restart();

        action.Should().NotThrow();
    }

    [Test]
    public void Restart_WhenHostApplicationLifetimeIsNull_DoesNotThrow()
    {
        var standaloneController = new SystemRestartController(this.runtimeInfo, null!);

        var action = () => standaloneController.Restart();

        action.Should().NotThrow();
        this.runtimeInfo.RestartPending.Should().BeTrue();
    }

    [Test]
    public void SystemRestartController_Attributes_AreDecoratedCorrectly()
    {
        var type = typeof(SystemRestartController);
        var apiAttribute = type.GetCustomAttribute<V1ApiControllerAttribute>();

        apiAttribute.Should().NotBeNull();
        apiAttribute!.Resource.Should().Be("system/restart");

        var method = type.GetMethod(nameof(SystemRestartController.Restart));
        method.Should().NotBeNull();

        var httpPostAttribute = method!.GetCustomAttribute<HttpPostAttribute>();
        httpPostAttribute.Should().NotBeNull();
    }
}

[TestFixture]
public class SystemShutdownControllerTest
{
    private IRuntimeInfo runtimeInfo = null!;
    private IHostApplicationLifetime hostApplicationLifetime = null!;
    private SystemShutdownController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.runtimeInfo = Substitute.For<IRuntimeInfo>();
        this.hostApplicationLifetime = Substitute.For<IHostApplicationLifetime>();
        this.controller = new SystemShutdownController(this.runtimeInfo, this.hostApplicationLifetime);
    }

    [Test]
    public void Shutdown_SetsRestartPendingToFalse_AndReturnsOk()
    {
        this.runtimeInfo.RestartPending = true;

        var result = this.controller.Shutdown();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(200);

        var messageProp = okResult.Value!.GetType().GetProperty("message")?.GetValue(okResult.Value);
        messageProp.Should().Be("Shutting down Leecharr...");

        this.runtimeInfo.RestartPending.Should().BeFalse();
    }

    [Test]
    public async Task Shutdown_TriggersStopApplicationAfterDelay()
    {
        this.controller.Shutdown();

        // SystemShutdownController delays for 500ms before stopping the application
        await Task.Delay(650);

        this.hostApplicationLifetime.Received(1).StopApplication();
    }

    [Test]
    public void Shutdown_WhenRuntimeInfoIsNull_DoesNotThrow()
    {
        var standaloneController = new SystemShutdownController(null!, this.hostApplicationLifetime);

        var action = () => standaloneController.Shutdown();

        action.Should().NotThrow();
    }

    [Test]
    public void Shutdown_WhenHostApplicationLifetimeIsNull_DoesNotThrow()
    {
        var standaloneController = new SystemShutdownController(this.runtimeInfo, null!);

        var action = () => standaloneController.Shutdown();

        action.Should().NotThrow();
        this.runtimeInfo.RestartPending.Should().BeFalse();
    }

    [Test]
    public void SystemShutdownController_Attributes_AreDecoratedCorrectly()
    {
        var type = typeof(SystemShutdownController);
        var apiAttribute = type.GetCustomAttribute<V1ApiControllerAttribute>();

        apiAttribute.Should().NotBeNull();
        apiAttribute!.Resource.Should().Be("system/shutdown");

        var method = type.GetMethod(nameof(SystemShutdownController.Shutdown));
        method.Should().NotBeNull();

        var httpPostAttribute = method!.GetCustomAttribute<HttpPostAttribute>();
        httpPostAttribute.Should().NotBeNull();
    }
}

[TestFixture]
public class SystemLifecycleCoordinationTest
{
    [Test]
    public void Lifecycle_RestartAndShutdownSequence_CoordinatesRestartPendingFlag()
    {
        var runtimeInfo = Substitute.For<IRuntimeInfo>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        var restartController = new SystemRestartController(runtimeInfo, lifetime);
        var shutdownController = new SystemShutdownController(runtimeInfo, lifetime);

        // Sequence: Restart pending true -> Shutdown pending false -> Restart pending true
        restartController.Restart();
        runtimeInfo.RestartPending.Should().BeTrue();

        shutdownController.Shutdown();
        runtimeInfo.RestartPending.Should().BeFalse();

        restartController.Restart();
        runtimeInfo.RestartPending.Should().BeTrue();
    }
}
