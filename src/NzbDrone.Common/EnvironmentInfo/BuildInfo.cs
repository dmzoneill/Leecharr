using System;
using System.IO;
using System.Reflection;
using NLog;

namespace NzbDrone.Common.EnvironmentInfo;

public static class BuildInfo
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    static BuildInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyVersion = assembly.GetName().Version ?? new Version(0, 1, 0);

        var versionFromFile = ReadVersionFile();
        Version = versionFromFile ?? assemblyVersion;
    }

    public static Version Version { get; }
    public static string AppName => "Leecharr";
    public static string Branch => "main";

    private static Version ReadVersionFile()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "version"),
            Path.Combine(Directory.GetCurrentDirectory(), "version"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(path).Trim();

                foreach (var line in content.Split('\n'))
                {
                    var trimmed = line.Trim();

                    if (trimmed.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                    {
                        var versionString = trimmed.Substring("version=".Length).Trim();
                        if (Version.TryParse(versionString, out var parsed))
                        {
                            return parsed;
                        }
                    }

                    if (Version.TryParse(trimmed, out var direct))
                    {
                        return direct;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or FormatException)
            {
                Logger.Warn(ex, "Could not read version file at {0}", path);
            }
        }

        return null;
    }
}
