// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.SystemServices;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class PowerManagementServiceTest
{
    [Test]
    public async Task ExecutePowerActionAsync_WithNone_ReturnsTrueWithoutWork()
    {
        var service = new PowerManagementService();
        var result = await service.ExecutePowerActionAsync(PowerAction.None);
        result.Should().BeTrue();
    }

    [Test]
    public void IsInContainer_DoesNotThrow()
    {
        var service = new PowerManagementService();
        _ = service.IsInContainer;
    }

    [Test]
    public async Task ExecutePowerActionAsync_WithExitApplicationAndHostLifetime_CallsStopApplication()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var service = new PowerManagementService(lifetime);

        var result = await service.ExecutePowerActionAsync(PowerAction.ExitApplication);

        result.Should().BeTrue();
        lifetime.Received(1).StopApplication();
    }

    [Test]
    public async Task ExecutePowerActionAsync_WithExitApplicationWithoutLifetime_ReturnsTrue()
    {
        var service = new PowerManagementService(null);

        var result = await service.ExecutePowerActionAsync(PowerAction.ExitApplication);

        result.Should().BeTrue();
    }

    [Test]
    public async Task ExecutePowerActionAsync_InsideContainer_StopsApplicationAndReturnsTrue()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var service = new PowerManagementService(lifetime)
        {
            ContainerDetector = () => true,
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Shutdown);

        result.Should().BeTrue();
        lifetime.Received(1).StopApplication();
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnWindows_Suspend_InvokesSetSuspendStateWithFalse()
    {
        bool? capturedHibernate = null;
        bool? capturedForce = null;
        bool? capturedDisableWake = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Windows,
            ContainerDetector = () => false,
            WindowsSetSuspendStateInvoker = (hibernate, forceCritical, disableWakeEvent) =>
            {
                capturedHibernate = hibernate;
                capturedForce = forceCritical;
                capturedDisableWake = disableWakeEvent;
                return true;
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Suspend);

        result.Should().BeTrue();
        capturedHibernate.Should().BeFalse();
        capturedForce.Should().BeFalse();
        capturedDisableWake.Should().BeFalse();
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnWindows_Hibernate_InvokesSetSuspendStateWithTrue()
    {
        bool? capturedHibernate = null;
        bool? capturedForce = null;
        bool? capturedDisableWake = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Windows,
            ContainerDetector = () => false,
            WindowsSetSuspendStateInvoker = (hibernate, forceCritical, disableWakeEvent) =>
            {
                capturedHibernate = hibernate;
                capturedForce = forceCritical;
                capturedDisableWake = disableWakeEvent;
                return true;
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Hibernate);

        result.Should().BeTrue();
        capturedHibernate.Should().BeTrue();
        capturedForce.Should().BeFalse();
        capturedDisableWake.Should().BeFalse();
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnWindows_SetSuspendStateThrows_ReturnsFalse()
    {
        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Windows,
            ContainerDetector = () => false,
            WindowsSetSuspendStateInvoker = (hibernate, forceCritical, disableWakeEvent) =>
            {
                throw new InvalidOperationException("P/Invoke failure");
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Suspend);

        result.Should().BeFalse();
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnWindows_Shutdown_CallsShutdownProcessWithCorrectArguments()
    {
        string executedCommand = null;
        string[] executedArguments = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Windows,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) =>
            {
                executedCommand = fileName;
                executedArguments = args;
                return Task.FromResult(true);
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Shutdown);

        result.Should().BeTrue();
        executedCommand.Should().Be("shutdown");
        executedArguments.Should().Equal(new[] { "/s", "/t", "60", "/c", "Leecharr completed queue" });
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnOsx_Shutdown_UsesOsascriptWithCleanArgumentList()
    {
        string executedCommand = null;
        string[] executedArguments = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.OSX,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) =>
            {
                executedCommand = fileName;
                executedArguments = args;
                return Task.FromResult(true);
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Shutdown);

        result.Should().BeTrue();
        executedCommand.Should().Be("osascript");
        executedArguments.Should().NotBeNull();
        executedArguments.Should().Equal(new[] { "-e", "tell app \"System Events\" to shut down" });
        executedArguments[1].Should().NotStartWith("'").And.NotEndWith("'");
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnOsx_Suspend_UsesPmsetSleepnow()
    {
        string executedCommand = null;
        string[] executedArguments = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.OSX,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) =>
            {
                executedCommand = fileName;
                executedArguments = args;
                return Task.FromResult(true);
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Suspend);

        result.Should().BeTrue();
        executedCommand.Should().Be("pmset");
        executedArguments.Should().Equal(new[] { "sleepnow" });
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnOsx_Hibernate_UsesPmsetSleepnow()
    {
        string executedCommand = null;
        string[] executedArguments = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.OSX,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) =>
            {
                executedCommand = fileName;
                executedArguments = args;
                return Task.FromResult(true);
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Hibernate);

        result.Should().BeTrue();
        executedCommand.Should().Be("pmset");
        executedArguments.Should().Equal(new[] { "sleepnow" });
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnLinux_Shutdown_UsesSystemctlPoweroff()
    {
        string executedCommand = null;
        string[] executedArguments = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Linux,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) =>
            {
                executedCommand = fileName;
                executedArguments = args;
                return Task.FromResult(true);
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Shutdown);

        result.Should().BeTrue();
        executedCommand.Should().Be("systemctl");
        executedArguments.Should().Equal(new[] { "poweroff" });
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnLinux_Suspend_UsesSystemctlSuspend()
    {
        string executedCommand = null;
        string[] executedArguments = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Linux,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) =>
            {
                executedCommand = fileName;
                executedArguments = args;
                return Task.FromResult(true);
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Suspend);

        result.Should().BeTrue();
        executedCommand.Should().Be("systemctl");
        executedArguments.Should().Equal(new[] { "suspend" });
    }

    [Test]
    public async Task ExecutePowerActionAsync_OnLinux_Hibernate_UsesSystemctlHibernate()
    {
        string executedCommand = null;
        string[] executedArguments = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Linux,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) =>
            {
                executedCommand = fileName;
                executedArguments = args;
                return Task.FromResult(true);
            },
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Hibernate);

        result.Should().BeTrue();
        executedCommand.Should().Be("systemctl");
        executedArguments.Should().Equal(new[] { "hibernate" });
    }

    [Test]
    public async Task ExecutePowerActionAsync_ProcessRunnerFails_ReturnsFalse()
    {
        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Linux,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) => Task.FromResult(false),
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Shutdown);

        result.Should().BeFalse();
    }

    [Test]
    public async Task ExecutePowerActionAsync_ProcessRunnerThrows_ReturnsFalse()
    {
        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Linux,
            ContainerDetector = () => false,
            ProcessRunner = (fileName, args) => throw new InvalidOperationException("Process failed"),
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Shutdown);

        result.Should().BeFalse();
    }

    [Test]
    public async Task ExecutePowerActionAsync_UnsupportedPlatform_ReturnsFalse()
    {
        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Create("FREEBSD"),
            ContainerDetector = () => false,
        };

        var result = await service.ExecutePowerActionAsync(PowerAction.Shutdown);

        result.Should().BeFalse();
    }
}
