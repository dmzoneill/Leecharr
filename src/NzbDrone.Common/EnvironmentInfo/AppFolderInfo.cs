// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;

namespace NzbDrone.Common.EnvironmentInfo;

public interface IAppFolderInfo
{
    string AppDataFolder { get; }

    string StartUpFolder { get; }
}

public class AppFolderInfo : IAppFolderInfo
{
    public AppFolderInfo(StartupContext startupContext)
    {
        if (startupContext?.Args != null && startupContext.Args.TryGetValue("data", out var dataDir) && !string.IsNullOrWhiteSpace(dataDir))
        {
            this.AppDataFolder = ExpandHome(dataDir);
        }
        else
        {
            var envAppData = Environment.GetEnvironmentVariable("LEECHARR__APP_DATA")
                ?? Environment.GetEnvironmentVariable("LEECHARR_APP_DATA");
            if (!string.IsNullOrWhiteSpace(envAppData))
            {
                this.AppDataFolder = ExpandHome(envAppData);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            {
                var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (!string.IsNullOrWhiteSpace(xdgConfigHome))
                {
                    this.AppDataFolder = Path.Combine(ExpandHome(xdgConfigHome), "Leecharr");
                }
                else
                {
                    var home = ResolveHomeDirectory();
                    this.AppDataFolder = Path.Combine(home, ".config", "Leecharr");
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var home = ResolveHomeDirectory();
                this.AppDataFolder = Path.Combine(home, ".config", "Leecharr");
            }
            else
            {
                this.AppDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Leecharr");
            }
        }

        this.StartUpFolder = AppDomain.CurrentDomain.BaseDirectory;

        try
        {
            Directory.CreateDirectory(this.AppDataFolder);
        }
        catch
        {
            // Ignore directory creation failures during mock/restricted runs
        }
    }

    public string AppDataFolder { get; }

    public string StartUpFolder { get; }

    private static string ResolveHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME");
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            home = OperatingSystem.IsWindows()
                ? (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) ?? "C:\\ProgramData")
                : (OperatingSystem.IsMacOS() ? "/Users/Shared" : "/root");
        }

        return home;
    }

    private static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path == "~")
        {
            return ResolveHomeDirectory();
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(ResolveHomeDirectory(), path[2..]);
        }

        return path;
    }
}
