// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Instrumentation;

/// <summary>
/// Provides file descriptor and handle metrics across Linux, macOS, and Windows.
/// </summary>
public class FileDescriptorProvider : IFileDescriptorProvider
{
    private readonly Logger logger;
    private readonly string procFdPath;
    private readonly string procLimitsPath;
    private readonly Func<int> handleCountAccessor;
    private readonly Func<bool> isWindowsAccessor;

    public FileDescriptorProvider()
        : this("/proc/self/fd", "/proc/self/limits", null, null)
    {
    }

    internal FileDescriptorProvider(
        string procFdPath,
        string procLimitsPath,
        Func<int> handleCountAccessor = null,
        Func<bool> isWindowsAccessor = null)
    {
        this.logger = LogManager.GetCurrentClassLogger();
        this.procFdPath = procFdPath ?? "/proc/self/fd";
        this.procLimitsPath = procLimitsPath ?? "/proc/self/limits";
        this.handleCountAccessor = handleCountAccessor ?? (() => Process.GetCurrentProcess().HandleCount);
        this.isWindowsAccessor = isWindowsAccessor ?? (() => OsInfo.IsWindows);
    }

    public int GetOpenFileDescriptorCount()
    {
        if (!this.isWindowsAccessor() && Directory.Exists(this.procFdPath))
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(this.procFdPath).Count();
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to count file descriptors from {0}", this.procFdPath);
            }
        }

        try
        {
            return this.handleCountAccessor();
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to retrieve handle count from process");
            return -1;
        }
    }

    public int GetMaxFileDescriptors()
    {
        if (!this.isWindowsAccessor() && File.Exists(this.procLimitsPath))
        {
            try
            {
                var lines = File.ReadLines(this.procLimitsPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Max open files", StringComparison.OrdinalIgnoreCase))
                    {
                        var remainder = line.Substring("Max open files".Length).Trim();
                        var parts = remainder.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length > 0)
                        {
                            if (string.Equals(parts[0], "unlimited", StringComparison.OrdinalIgnoreCase))
                            {
                                return -1;
                            }

                            if (int.TryParse(parts[0], out var softLimit))
                            {
                                return softLimit;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to parse max open files limit from {0}", this.procLimitsPath);
            }
        }

        return -1;
    }

    public double? GetFileDescriptorUsagePercentage()
    {
        var openCount = this.GetOpenFileDescriptorCount();
        var maxCount = this.GetMaxFileDescriptors();

        if (openCount <= 0 || maxCount <= 0)
        {
            return null;
        }

        return Math.Min(100.0, Math.Max(0.0, (double)openCount / maxCount * 100.0));
    }
}
