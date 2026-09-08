// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.SystemServices;

public class PowerManagementService : IPowerManagementService
{
    private readonly IHostApplicationLifetime hostLifetime;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public PowerManagementService(IHostApplicationLifetime hostLifetime = null)
    {
        this.hostLifetime = hostLifetime;
    }

    internal Func<string, string[], Task<bool>> ProcessRunner { get; set; }

    internal Func<bool, bool, bool, bool> WindowsSetSuspendStateInvoker { get; set; }

    internal Func<bool> ContainerDetector { get; set; }

    internal OSPlatform? TargetPlatformOverride { get; set; }

    public bool IsInContainer
    {
        get
        {
            if (this.ContainerDetector != null)
            {
                return this.ContainerDetector();
            }

            try
            {
                return File.Exists("/.dockerenv") ||
                       (File.Exists("/proc/1/cgroup") && File.ReadAllText("/proc/1/cgroup").Contains("docker"));
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<bool> ExecutePowerActionAsync(PowerAction action)
    {
        if (action == PowerAction.None)
        {
            return true;
        }

        this.logger.Info("Executing power management action: {0}", action);

        if (this.IsInContainer && action != PowerAction.ExitApplication)
        {
            this.logger.Warn("Host power actions ({0}) are restricted inside container environment. Exiting process instead.", action);
            this.StopApplication();
            return true;
        }

        try
        {
            if (action == PowerAction.ExitApplication)
            {
                this.StopApplication();
                return true;
            }

            if (this.IsPlatform(OSPlatform.Linux))
            {
                return await this.ExecuteLinuxPowerActionAsync(action);
            }

            if (this.IsPlatform(OSPlatform.Windows))
            {
                return await this.ExecuteWindowsPowerActionAsync(action);
            }

            if (this.IsPlatform(OSPlatform.OSX))
            {
                return await this.ExecuteOsxPowerActionAsync(action);
            }

            this.logger.Warn("Power management is not supported on OS platform {0}", RuntimeInformation.OSDescription);
            return false;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to execute power action: {0}", action);
            return false;
        }
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    private bool IsPlatform(OSPlatform platform)
    {
        if (this.TargetPlatformOverride.HasValue)
        {
            return this.TargetPlatformOverride.Value == platform;
        }

        return RuntimeInformation.IsOSPlatform(platform);
    }

    private void StopApplication()
    {
        if (this.hostLifetime != null)
        {
            this.logger.Info("Requesting graceful host application shutdown");
            this.hostLifetime.StopApplication();
        }
        else
        {
            this.logger.Warn("IHostApplicationLifetime is not available to gracefully stop application");
        }
    }

    private async Task<bool> ExecuteLinuxPowerActionAsync(PowerAction action)
    {
        var (cmd, args) = action switch
        {
            PowerAction.Shutdown => ("systemctl", new[] { "poweroff" }),
            PowerAction.Suspend => ("systemctl", new[] { "suspend" }),
            PowerAction.Hibernate => ("systemctl", new[] { "hibernate" }),
            _ => (null, null),
        };

        if (cmd == null)
        {
            return false;
        }

        return await this.RunProcessAsync(cmd, args);
    }

    private async Task<bool> ExecuteWindowsPowerActionAsync(PowerAction action)
    {
        switch (action)
        {
            case PowerAction.Shutdown:
                return await this.RunProcessAsync("shutdown", new[] { "/s", "/t", "60", "/c", "Leecharr completed queue" });
            case PowerAction.Suspend:
                return this.InvokeWindowsSetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
            case PowerAction.Hibernate:
                return this.InvokeWindowsSetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: false);
            default:
                return false;
        }
    }

    private bool InvokeWindowsSetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent)
    {
        try
        {
            if (this.WindowsSetSuspendStateInvoker != null)
            {
                return this.WindowsSetSuspendStateInvoker(hibernate, forceCritical, disableWakeEvent);
            }

            return SetSuspendState(hibernate, forceCritical, disableWakeEvent);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to invoke Windows SetSuspendState(hibernate={0}, forceCritical={1}, disableWakeEvent={2})", hibernate, forceCritical, disableWakeEvent);
            return false;
        }
    }

    private async Task<bool> ExecuteOsxPowerActionAsync(PowerAction action)
    {
        var (cmd, args) = action switch
        {
            PowerAction.Shutdown => ("osascript", new[] { "-e", "tell app \"System Events\" to shut down" }),
            PowerAction.Suspend => ("pmset", new[] { "sleepnow" }),
            PowerAction.Hibernate => ("pmset", new[] { "sleepnow" }),
            _ => (null, null),
        };

        if (cmd == null)
        {
            return false;
        }

        return await this.RunProcessAsync(cmd, args);
    }

    private async Task<bool> RunProcessAsync(string fileName, string[] arguments)
    {
        if (this.ProcessRunner != null)
        {
            return await this.ProcessRunner(fileName, arguments);
        }

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (arguments != null)
            {
                foreach (var arg in arguments)
                {
                    proc.StartInfo.ArgumentList.Add(arg);
                }
            }

            proc.Start();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to run power command '{0} {1}'", fileName, arguments != null ? string.Join(" ", arguments) : string.Empty);
            return false;
        }
    }
}
