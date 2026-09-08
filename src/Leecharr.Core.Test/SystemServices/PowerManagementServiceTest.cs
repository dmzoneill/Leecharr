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

    [Test]
    public void InhibitSleep_IncrementsLeaseCount_AndReturnsDisposable()
    {
        var service = new PowerManagementService
        {
            ContainerDetector = () => true,
        };

        service.ActiveSleepInhibitionLeases.Should().Be(0);
        service.IsSleepInhibited.Should().BeFalse();

        using (var token = service.InhibitSleep("Download in progress"))
        {
            token.Should().NotBeNull();
            service.ActiveSleepInhibitionLeases.Should().Be(1);
            service.IsSleepInhibited.Should().BeTrue();
        }

        service.ActiveSleepInhibitionLeases.Should().Be(0);
        service.IsSleepInhibited.Should().BeFalse();
    }

    [Test]
    public void InhibitSleep_MultipleLeases_TracksRefCountAndReleasesOnlyWhenAllDisposed()
    {
        var service = new PowerManagementService
        {
            ContainerDetector = () => true,
        };

        var token1 = service.InhibitSleep("Download 1");
        service.ActiveSleepInhibitionLeases.Should().Be(1);
        service.IsSleepInhibited.Should().BeTrue();

        var token2 = service.InhibitSleep("Download 2");
        service.ActiveSleepInhibitionLeases.Should().Be(2);
        service.IsSleepInhibited.Should().BeTrue();

        token1.Dispose();
        service.ActiveSleepInhibitionLeases.Should().Be(1);
        service.IsSleepInhibited.Should().BeTrue();

        // Idempotent dispose
        token1.Dispose();
        service.ActiveSleepInhibitionLeases.Should().Be(1);
        service.IsSleepInhibited.Should().BeTrue();

        token2.Dispose();
        service.ActiveSleepInhibitionLeases.Should().Be(0);
        service.IsSleepInhibited.Should().BeFalse();
    }

    [Test]
    public void SetSleepInhibited_TrueAndFalse_UpdatesInhibitionStateAndLeases()
    {
        var service = new PowerManagementService
        {
            ContainerDetector = () => true,
        };

        service.SetSleepInhibited(true, "Manual enable");
        service.IsSleepInhibited.Should().BeTrue();
        service.ActiveSleepInhibitionLeases.Should().Be(1);

        // Calling true again when already inhibited does not create redundant leases
        service.SetSleepInhibited(true, "Manual enable 2");
        service.IsSleepInhibited.Should().BeTrue();
        service.ActiveSleepInhibitionLeases.Should().Be(1);

        service.SetSleepInhibited(false, "Manual disable");
        service.IsSleepInhibited.Should().BeFalse();
        service.ActiveSleepInhibitionLeases.Should().Be(0);
    }

    [Test]
    public void InhibitSleep_OnWindows_CallsSetThreadExecutionStateWithRequiredFlagsAndContinuousOnRelease()
    {
        var capturedFlags = new List<uint>();

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Windows,
            ContainerDetector = () => false,
            WindowsSetExecutionStateInvoker = flags =>
            {
                capturedFlags.Add(flags);
                return 1;
            },
        };

        using (service.InhibitSleep("Active torrent"))
        {
            capturedFlags.Should().HaveCount(1);
            // Should contain ES_CONTINUOUS (0x80000000) and ES_SYSTEM_REQUIRED (0x1)
            (capturedFlags[0] & 0x80000000).Should().NotBe(0);
            (capturedFlags[0] & 0x00000001).Should().NotBe(0);
        }

        capturedFlags.Should().HaveCount(2);
        capturedFlags[1].Should().Be(0x80000000); // ES_CONTINUOUS alone to reset
    }

    [Test]
    public void InhibitSleep_OnOsx_CallsCreateAndReleaseAssertion()
    {
        string capturedType = null;
        string capturedReason = null;
        uint? releasedAssertionId = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.OSX,
            ContainerDetector = () => false,
            OsxCreateAssertionInvoker = (type, reason) =>
            {
                capturedType = type;
                capturedReason = reason;
                return 42;
            },
            OsxReleaseAssertionInvoker = assertionId =>
            {
                releasedAssertionId = assertionId;
                return true;
            },
        };

        using (service.InhibitSleep("Seeding torrent"))
        {
            service.IsSleepInhibited.Should().BeTrue();
            capturedType.Should().Be("PreventUserIdleSystemSleep");
            capturedReason.Should().Be("Seeding torrent");
        }

        service.IsSleepInhibited.Should().BeFalse();
        releasedAssertionId.Should().Be(42);
    }

    [Test]
    public void InhibitSleep_OnLinux_StartsAndStopsProcess()
    {
        string executedCommand = null;
        string[] executedArgs = null;

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Linux,
            ContainerDetector = () => false,
            LinuxProcessStarter = (cmd, args) =>
            {
                executedCommand = cmd;
                executedArgs = args;
                // Return a dummy stopped process or mock
                return new System.Diagnostics.Process();
            },
        };

        using (service.InhibitSleep("Linux torrent download"))
        {
            service.IsSleepInhibited.Should().BeTrue();
            executedCommand.Should().Be("systemd-inhibit");
            executedArgs.Should().Contain("--what=idle:sleep");
            executedArgs.Should().Contain("--why=Linux torrent download");
        }

        service.IsSleepInhibited.Should().BeFalse();
    }

    [Test]
    public void Dispose_WhenInhibited_SafelyReleasesResources()
    {
        var capturedFlags = new List<uint>();

        var service = new PowerManagementService
        {
            TargetPlatformOverride = OSPlatform.Windows,
            ContainerDetector = () => false,
            WindowsSetExecutionStateInvoker = flags =>
            {
                capturedFlags.Add(flags);
                return 1;
            },
        };

        _ = service.InhibitSleep("Will be disposed");
        service.IsSleepInhibited.Should().BeTrue();

        service.Dispose();

        service.IsSleepInhibited.Should().BeFalse();
        service.ActiveSleepInhibitionLeases.Should().Be(0);
        capturedFlags.Should().HaveCount(2);
        capturedFlags[1].Should().Be(0x80000000); // ES_CONTINUOUS
    }

    [Test]
    public void IsInContainer_WhenDotnetRunningInContainerEnvVarSet_ReturnsTrue()
    {
        var original = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
            var service = new PowerManagementService();
            service.IsInContainer.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", original);
        }
    }

    [Test]
    public void IsInContainer_WhenContainerEnvVarSet_ReturnsTrue()
    {
        var original = Environment.GetEnvironmentVariable("container");
        try
        {
            Environment.SetEnvironmentVariable("container", "podman");
            var service = new PowerManagementService();
            service.IsInContainer.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("container", original);
        }
    }

    [Test]
    public void IsInContainer_WhenKubernetesServiceHostEnvVarSet_ReturnsTrue()
    {
        var original = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        try
        {
            Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "10.0.0.1");
            var service = new PowerManagementService();
            service.IsInContainer.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", original);
        }
    }
}
