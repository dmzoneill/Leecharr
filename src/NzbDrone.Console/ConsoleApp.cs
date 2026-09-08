// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
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
        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                Logger.Fatal(ex, "Unhandled AppDomain exception: {0}", ex.Message);
            }
            else
            {
                Logger.Fatal("Unhandled AppDomain exception: {0}", eventArgs.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
        {
            Logger.Error(eventArgs.Exception, "Unobserved task exception: {0}", eventArgs.Exception.Message);
            eventArgs.SetObserved();
        };

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
