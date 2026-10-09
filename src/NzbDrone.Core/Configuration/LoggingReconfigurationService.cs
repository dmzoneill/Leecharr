// Copyright (c) FeedItOut. All rights reserved.

using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public class LoggingReconfigurationService :
    IHandle<ApplicationStartedEvent>,
    IHandle<ConfigSavedEvent>,
    IHandle<ConfigFileSavedEvent>
{
    private readonly IConfigService _configService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly Logger _logger;

    public LoggingReconfigurationService(IConfigService configService, IConfigFileProvider configFileProvider)
    {
        _configService = configService;
        _configFileProvider = configFileProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message) => ApplyLoggingConfiguration();

    public void Handle(ConfigSavedEvent message) => ApplyLoggingConfiguration();

    public void Handle(ConfigFileSavedEvent message) => ApplyLoggingConfiguration();

    private void ApplyLoggingConfiguration()
    {
        var consoleLevel = _configFileProvider.LogLevel;
        if (string.IsNullOrWhiteSpace(consoleLevel))
        {
            consoleLevel = "Info";
        }

        var fileLevel = "Off";
        if (_configService.LogToFile)
        {
            fileLevel = _configService.FileLogLevel;
            if (string.IsNullOrWhiteSpace(fileLevel))
            {
                fileLevel = "Info";
            }
        }

        NzbDroneLogger.Reconfigure(consoleLevel, fileLevel);
        _logger.Debug("Logging reconfigured: consoleLevel={0}, fileLevel={1}", consoleLevel, fileLevel);
    }
}
