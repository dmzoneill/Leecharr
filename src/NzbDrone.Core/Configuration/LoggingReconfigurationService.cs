// Copyright (c) PlaceholderCompany. All rights reserved.

using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public class LoggingReconfigurationService : IHandle<ConfigSavedEvent>
{
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public LoggingReconfigurationService(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ConfigSavedEvent message)
    {
        var consoleLevel = _configService.DebugMode ? "Debug" : "Info";
        var fileLevel = _configService.LogToFile ? (_configService.FileLogLevel ?? "Info") : "Off";

        NzbDroneLogger.Reconfigure(consoleLevel, fileLevel);
        _logger.Debug("Logging reconfigured: consoleLevel={0}, fileLevel={1}", consoleLevel, fileLevel);
    }
}
