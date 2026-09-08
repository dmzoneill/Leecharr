// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Host;

namespace NzbDrone.Console;

public static class ConsoleApp
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static void Main(string[] args)
    {
        try
        {
            var startupContext = new StartupContext(args);
            var appFolderInfo = new AppFolderInfo(startupContext);
            NzbDroneLogger.Register(startupContext, appFolderInfo);

            Logger.Info("Starting Leecharr Console - {0}", BuildInfo.Version);
            Bootstrap.Start(startupContext);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine("Leecharr failed to start: " + ex.Message);
            Logger.Fatal(ex, "Failed to start Leecharr");
            Environment.ExitCode = 1;
        }
    }
}
