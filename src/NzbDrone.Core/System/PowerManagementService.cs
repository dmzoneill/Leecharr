// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.SystemServices;

public class PowerManagementService : IPowerManagementService
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public bool IsInContainer
    {
        get
        {
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
            Environment.Exit(0);
            return true;
        }

        try
        {
            if (action == PowerAction.ExitApplication)
            {
                Environment.Exit(0);
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await this.ExecuteLinuxPowerActionAsync(action);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await this.ExecuteWindowsPowerActionAsync(action);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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

    private async Task<bool> ExecuteLinuxPowerActionAsync(PowerAction action)
    {
        var (cmd, args) = action switch
        {
            PowerAction.Shutdown => ("systemctl", "poweroff"),
            PowerAction.Suspend => ("systemctl", "suspend"),
            PowerAction.Hibernate => ("systemctl", "hibernate"),
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
        var (cmd, args) = action switch
        {
            PowerAction.Shutdown => ("shutdown", "/s /t 60 /c \"Leecharr completed queue\""),
            PowerAction.Suspend => ("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"),
            PowerAction.Hibernate => ("shutdown", "/h"),
            _ => (null, null),
        };

        if (cmd == null)
        {
            return false;
        }

        return await this.RunProcessAsync(cmd, args);
    }

    private async Task<bool> ExecuteOsxPowerActionAsync(PowerAction action)
    {
        var (cmd, args) = action switch
        {
            PowerAction.Shutdown => ("osascript", "-e 'tell app \"System Events\" to shut down'"),
            PowerAction.Suspend => ("pmset", "sleepnow"),
            _ => (null, null),
        };

        if (cmd == null)
        {
            return false;
        }

        return await this.RunProcessAsync(cmd, args);
    }

    private async Task<bool> RunProcessAsync(string fileName, string arguments)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            proc.Start();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to run power command '{0} {1}'", fileName, arguments);
            return false;
        }
    }
}
