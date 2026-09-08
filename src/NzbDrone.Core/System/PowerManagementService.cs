// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.SystemServices;

public class PowerManagementService : IPowerManagementService, IDisposable
{
    [Flags]
    internal enum ExecutionState : uint
    {
        EsSystemRequired = 0x00000001,
        EsDisplayRequired = 0x00000002,
        EsUserPresent = 0x00000004,
        EsAwaymodeRequired = 0x00000040,
        EsContinuous = 0x80000000,
    }

    private readonly IHostApplicationLifetime hostLifetime;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly object lockObj = new();

    private int activeLeaseCount;
    private bool isSleepInhibited;
    private uint osxAssertionId;
    private Process linuxInhibitProcess;
    private bool disposed;

    public PowerManagementService(IHostApplicationLifetime hostLifetime = null)
    {
        this.hostLifetime = hostLifetime;
    }

    internal Func<string, string[], Task<bool>> ProcessRunner { get; set; }

    internal Func<bool, bool, bool, bool> WindowsSetSuspendStateInvoker { get; set; }

    internal Func<uint, uint> WindowsSetExecutionStateInvoker { get; set; }

    internal Func<string, string, uint> OsxCreateAssertionInvoker { get; set; }

    internal Func<uint, bool> OsxReleaseAssertionInvoker { get; set; }

    internal Func<string, string[], Process> LinuxProcessStarter { get; set; }

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

    public bool IsSleepInhibited
    {
        get
        {
            lock (this.lockObj)
            {
                return this.isSleepInhibited;
            }
        }
    }

    public int ActiveSleepInhibitionLeases
    {
        get
        {
            lock (this.lockObj)
            {
                return this.activeLeaseCount;
            }
        }
    }

    public IDisposable InhibitSleep(string reason)
    {
        lock (this.lockObj)
        {
            this.activeLeaseCount++;
            if (this.activeLeaseCount == 1)
            {
                this.ApplySleepInhibition(true, reason);
            }
        }

        return new SleepInhibitionToken(this, reason);
    }

    public void SetSleepInhibited(bool inhibit, string reason)
    {
        lock (this.lockObj)
        {
            if (inhibit)
            {
                if (this.activeLeaseCount == 0)
                {
                    this.activeLeaseCount = 1;
                    this.ApplySleepInhibition(true, reason);
                }
            }
            else
            {
                if (this.activeLeaseCount > 0 || this.isSleepInhibited)
                {
                    this.activeLeaseCount = 0;
                    this.ApplySleepInhibition(false, reason);
                }
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

    public void Dispose()
    {
        lock (this.lockObj)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            if (this.isSleepInhibited)
            {
                try
                {
                    this.ApplySleepInhibition(false, "Service disposed");
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Error releasing sleep inhibition during disposal");
                }
            }

            this.activeLeaseCount = 0;
        }
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", SetLastError = true)]
    private static extern int IOPMAssertionCreateWithName(
        IntPtr assertionType,
        uint assertionLevel,
        IntPtr assertionName,
        out uint assertionId);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", SetLastError = true)]
    private static extern int IOPMAssertionRelease(uint assertionId);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", SetLastError = true)]
    private static extern IntPtr CFStringCreateWithCharacters(
        IntPtr alloc,
        [MarshalAs(UnmanagedType.LPWStr)] string str,
        IntPtr length);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", SetLastError = true)]
    private static extern void CFRelease(IntPtr cf);

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

    private void ReleaseSleepLease(string reason)
    {
        lock (this.lockObj)
        {
            if (this.disposed)
            {
                return;
            }

            if (this.activeLeaseCount > 0)
            {
                this.activeLeaseCount--;
                if (this.activeLeaseCount == 0)
                {
                    this.ApplySleepInhibition(false, reason);
                }
            }
        }
    }

    private void ApplySleepInhibition(bool inhibit, string reason)
    {
        if (this.IsInContainer)
        {
            this.logger.Debug("Sleep inhibition is skipped inside container environment");
            this.isSleepInhibited = inhibit;
            return;
        }

        this.logger.Info("Setting OS sleep inhibition: {0} (reason: {1})", inhibit ? "ENABLED" : "DISABLED", reason);

        try
        {
            if (this.IsPlatform(OSPlatform.Windows))
            {
                this.ApplyWindowsSleepInhibition(inhibit);
                return;
            }

            if (this.IsPlatform(OSPlatform.OSX))
            {
                this.ApplyOsxSleepInhibition(inhibit, reason);
                return;
            }

            if (this.IsPlatform(OSPlatform.Linux))
            {
                this.ApplyLinuxSleepInhibition(inhibit, reason);
                return;
            }

            this.logger.Debug("Sleep inhibition not supported on OS platform {0}", RuntimeInformation.OSDescription);
            this.isSleepInhibited = inhibit;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to apply sleep inhibition ({0})", inhibit);
        }
    }

    private void ApplyWindowsSleepInhibition(bool inhibit)
    {
        if (inhibit)
        {
            var flags = (uint)(ExecutionState.EsContinuous | ExecutionState.EsSystemRequired | ExecutionState.EsAwaymodeRequired);
            uint result;
            if (this.WindowsSetExecutionStateInvoker != null)
            {
                result = this.WindowsSetExecutionStateInvoker(flags);
            }
            else
            {
                result = SetThreadExecutionState(flags);
                if (result == 0)
                {
                    flags = (uint)(ExecutionState.EsContinuous | ExecutionState.EsSystemRequired);
                    result = SetThreadExecutionState(flags);
                }
            }

            this.isSleepInhibited = result != 0;
        }
        else
        {
            var flags = (uint)ExecutionState.EsContinuous;
            if (this.WindowsSetExecutionStateInvoker != null)
            {
                this.WindowsSetExecutionStateInvoker(flags);
            }
            else
            {
                SetThreadExecutionState(flags);
            }

            this.isSleepInhibited = false;
        }
    }

    private void ApplyOsxSleepInhibition(bool inhibit, string reason)
    {
        if (inhibit)
        {
            this.osxAssertionId = this.CreateOsxAssertion(reason);
            this.isSleepInhibited = this.osxAssertionId != 0;
        }
        else
        {
            if (this.osxAssertionId != 0)
            {
                this.ReleaseOsxAssertion(this.osxAssertionId);
                this.osxAssertionId = 0;
            }

            this.isSleepInhibited = false;
        }
    }

    private uint CreateOsxAssertion(string reason)
    {
        if (this.OsxCreateAssertionInvoker != null)
        {
            return this.OsxCreateAssertionInvoker("PreventUserIdleSystemSleep", reason);
        }

        try
        {
            var typeStr = "PreventUserIdleSystemSleep";
            var nameStr = string.IsNullOrWhiteSpace(reason) ? "Leecharr active download" : $"Leecharr: {reason}";

            var cfType = CFStringCreateWithCharacters(IntPtr.Zero, typeStr, (IntPtr)typeStr.Length);
            var cfName = CFStringCreateWithCharacters(IntPtr.Zero, nameStr, (IntPtr)nameStr.Length);

            try
            {
                var ret = IOPMAssertionCreateWithName(cfType, 255 /* kIOPMAssertionLevelOn */, cfName, out var assertionId);
                if (ret == 0)
                {
                    return assertionId;
                }

                this.logger.Warn("Failed to create macOS IOPMAssertion, return code: {0}", ret);
                return 0;
            }
            finally
            {
                if (cfType != IntPtr.Zero)
                {
                    CFRelease(cfType);
                }

                if (cfName != IntPtr.Zero)
                {
                    CFRelease(cfName);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to invoke macOS IOPMAssertionCreateWithName");
            return 0;
        }
    }

    private bool ReleaseOsxAssertion(uint assertionId)
    {
        if (assertionId == 0)
        {
            return true;
        }

        if (this.OsxReleaseAssertionInvoker != null)
        {
            return this.OsxReleaseAssertionInvoker(assertionId);
        }

        try
        {
            var ret = IOPMAssertionRelease(assertionId);
            return ret == 0;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to release macOS IOPMAssertion {0}", assertionId);
            return false;
        }
    }

    private void ApplyLinuxSleepInhibition(bool inhibit, string reason)
    {
        if (inhibit)
        {
            this.linuxInhibitProcess = this.StartLinuxSleepInhibition(reason);
            this.isSleepInhibited = this.linuxInhibitProcess != null;
        }
        else
        {
            if (this.linuxInhibitProcess != null)
            {
                this.StopLinuxSleepInhibition(this.linuxInhibitProcess);
                this.linuxInhibitProcess = null;
            }

            this.isSleepInhibited = false;
        }
    }

    private Process StartLinuxSleepInhibition(string reason)
    {
        var safeReason = string.IsNullOrWhiteSpace(reason) ? "Leecharr active download" : reason;
        var args = new[] { "--what=idle:sleep", "--who=Leecharr", $"--why={safeReason}", "sleep", "infinity" };

        if (this.LinuxProcessStarter != null)
        {
            return this.LinuxProcessStarter("systemd-inhibit", args);
        }

        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "systemd-inhibit",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var arg in args)
            {
                proc.StartInfo.ArgumentList.Add(arg);
            }

            proc.Start();
            return proc;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Could not start systemd-inhibit process for sleep inhibition");
            return null;
        }
    }

    private void StopLinuxSleepInhibition(Process process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to kill Linux sleep inhibition process");
        }
        finally
        {
            try
            {
                process.Dispose();
            }
            catch
            {
            }
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

    private sealed class SleepInhibitionToken : IDisposable
    {
        private readonly string reason;
        private PowerManagementService service;
        private int disposed;

        public SleepInhibitionToken(PowerManagementService service, string reason)
        {
            this.service = service;
            this.reason = reason;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 0)
            {
                this.service?.ReleaseSleepLease(this.reason);
                this.service = null;
            }
        }
    }
}
