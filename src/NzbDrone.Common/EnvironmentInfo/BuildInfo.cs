// Copyright (c) FeedItOut. All rights reserved.

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
        var path = Path.Combine(AppContext.BaseDirectory, "version");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ParseVersionFileContent(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            Logger.Warn(ex, "Could not read version file at {0}", path);
            return null;
        }
    }

    internal static Version ParseVersionFileContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!trimmed.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versionString = trimmed.Substring("version=".Length).Trim();
            if (Version.TryParse(versionString, out var parsed))
            {
                return parsed;
            }
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Version.TryParse(trimmed, out var direct))
            {
                return direct;
            }
        }

        return null;
    }
}
