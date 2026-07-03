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
        if (startupContext.Args.TryGetValue("data", out var dataDir))
        {
            AppDataFolder = dataDir;
        }
        else
        {
            AppDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Leecharr");
        }

        StartUpFolder = AppDomain.CurrentDomain.BaseDirectory;

        Directory.CreateDirectory(AppDataFolder);
    }

    public string AppDataFolder { get; }
    public string StartUpFolder { get; }
}
