using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Instrumentation;

public static class NzbDroneLogger
{
    public static void Register(StartupContext startupContext = null)
    {
        var config = new LoggingConfiguration();

        var consoleTarget = new ColoredConsoleTarget("console")
        {
            Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.f}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}"
        };

        config.AddTarget(consoleTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);

        var ringBufferTarget = new RingBufferTarget(2048) { Name = "ringBuffer" };
        RingBufferTarget.Instance = ringBufferTarget;
        config.AddTarget(ringBufferTarget);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, ringBufferTarget);

        LogManager.Configuration = config;
    }
}
