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
            NzbDroneLogger.Register(startupContext);

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
