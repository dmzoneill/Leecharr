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
        var envAppData = Environment.GetEnvironmentVariable("LEECHARR__APP_DATA");
        if (!string.IsNullOrWhiteSpace(envAppData))
        {
            AppDataFolder = envAppData;
        }
        else if (startupContext?.Args != null && startupContext.Args.TryGetValue("data", out var dataDir) && !string.IsNullOrWhiteSpace(dataDir))
        {
            AppDataFolder = dataDir;
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdgConfigHome))
            {
                AppDataFolder = Path.Combine(xdgConfigHome, "Leecharr");
            }
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                {
                    home = Environment.GetEnvironmentVariable("HOME") ?? "/root";
                }

                AppDataFolder = Path.Combine(home, ".config", "Leecharr");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? "~";
            }

            AppDataFolder = Path.Combine(home, ".config", "Leecharr");
        }
        else
        {
            AppDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Leecharr");
        }

        StartUpFolder = AppDomain.CurrentDomain.BaseDirectory;

        try
        {
            Directory.CreateDirectory(AppDataFolder);
        }
        catch
        {
            // Ignore directory creation failures during mock/restricted runs
        }
    }

    public string AppDataFolder { get; }
    public string StartUpFolder { get; }
}
