using System;
using System.IO;
using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Instrumentation;

public static class NzbDroneLogger
{
    public static void Register(StartupContext startupContext = null, IAppFolderInfo appFolderInfo = null)
    {
        var config = new LoggingConfiguration();

        var consoleLevel = LogLevel.Info;
        if (startupContext != null)
        {
            if (startupContext.Args.TryGetValue("log-level", out var levelArg))
            {
                if (levelArg.Equals("debug", StringComparison.OrdinalIgnoreCase))
                {
                    consoleLevel = LogLevel.Debug;
                }
                else if (levelArg.Equals("trace", StringComparison.OrdinalIgnoreCase))
                {
                    consoleLevel = LogLevel.Trace;
                }
            }
            else if (startupContext.Flags.Contains("d") || startupContext.Flags.Contains("debug"))
            {
                consoleLevel = LogLevel.Debug;
            }
        }

        var consoleTarget = new ColoredConsoleTarget("console")
        {
            Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.f}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}"
        };

        config.AddTarget(consoleTarget);
        config.AddRule(consoleLevel, LogLevel.Fatal, consoleTarget);

        var ringBufferTarget = new RingBufferTarget(4096) { Name = "ringBuffer" };
        RingBufferTarget.Instance = ringBufferTarget;
        config.AddTarget(ringBufferTarget);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, ringBufferTarget);

        var logDir = appFolderInfo != null
            ? Path.Combine(appFolderInfo.AppDataFolder, "logs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr", "logs");

        try
        {
            Directory.CreateDirectory(logDir);
            var fileTarget = new FileTarget("file")
            {
                FileName = Path.Combine(logDir, "leecharr.txt"),
                ArchiveFileName = Path.Combine(logDir, "leecharr.{#}.txt"),
                ArchiveEvery = FileArchivePeriod.Day,
                MaxArchiveFiles = 7,
                Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.f}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}"
            };

            config.AddTarget(fileTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
        }
        catch
        {
        }

        LogManager.Configuration = config;
    }

    public static void Reconfigure(string consoleLevelStr, string fileLevelStr)
    {
        var config = LogManager.Configuration;
        if (config == null)
        {
            return;
        }

        var consoleTarget = config.FindTargetByName<ColoredConsoleTarget>("console");
        if (consoleTarget != null && !string.IsNullOrWhiteSpace(consoleLevelStr))
        {
            try
            {
                var lvl = LogLevel.FromString(consoleLevelStr);
                var rulesToRemove = config.LoggingRules.Where(r => r.Targets.Contains(consoleTarget)).ToList();
                foreach (var rule in rulesToRemove)
                {
                    config.LoggingRules.Remove(rule);
                }

                config.AddRule(lvl, LogLevel.Fatal, consoleTarget);
            }
            catch
            {
            }
        }

        var fileTarget = config.FindTargetByName<FileTarget>("file");
        if (fileTarget != null && !string.IsNullOrWhiteSpace(fileLevelStr))
        {
            try
            {
                var lvl = LogLevel.FromString(fileLevelStr);
                var rulesToRemove = config.LoggingRules.Where(r => r.Targets.Contains(fileTarget)).ToList();
                foreach (var rule in rulesToRemove)
                {
                    config.LoggingRules.Remove(rule);
                }

                config.AddRule(lvl, LogLevel.Fatal, fileTarget);
            }
            catch
            {
            }
        }

        LogManager.ReconfigExistingLoggers();
    }
}
