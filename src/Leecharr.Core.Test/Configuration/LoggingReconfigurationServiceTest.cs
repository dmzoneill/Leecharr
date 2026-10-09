// Copyright (c) FeedItOut. All rights reserved.

using System.IO;
using System.Linq;
using FluentAssertions;
using NLog;
using NLog.Targets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;

namespace Leecharr.Core.Test.Configuration;

[TestFixture]
public class LoggingReconfigurationServiceTest
{
    private string tempAppDataFolder = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private LoggingReconfigurationService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempAppDataFolder = Path.Combine(Path.GetTempPath(), "Leecharr_LogReconfigTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(this.tempAppDataFolder);

        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempAppDataFolder);
        NzbDroneLogger.Register(startupContext: null, appFolderInfo: appFolderInfo);

        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService.LogToFile.Returns(true);
        this.configService.FileLogLevel.Returns("Warn");
        this.configService.DebugMode.Returns(true);

        this.service = new LoggingReconfigurationService(this.configService, this.configFileProvider);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempAppDataFolder))
        {
            try
            {
                Directory.Delete(this.tempAppDataFolder, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public void Handle_ConfigSavedEvent_UsesConfigFileLogLevelForConsole_NotDebugMode()
    {
        this.configFileProvider.LogLevel.Returns("trace");

        this.service.Handle(new ConfigSavedEvent());

        var config = LogManager.Configuration;
        config.Should().NotBeNull();

        var consoleTarget = config!.FindTargetByName<ColoredConsoleTarget>("console");
        var consoleRules = config.LoggingRules.Where(r => r.Targets.Contains(consoleTarget)).ToList();
        consoleRules.Any(r => r.Levels.Contains(LogLevel.Trace)).Should().BeTrue();
    }

    [Test]
    public void Handle_ApplicationStartedEvent_AppliesConfigFileLogLevel()
    {
        this.configFileProvider.LogLevel.Returns("error");

        this.service.Handle(new ApplicationStartedEvent());

        var config = LogManager.Configuration;
        var consoleTarget = config!.FindTargetByName<ColoredConsoleTarget>("console");
        var consoleRules = config.LoggingRules.Where(r => r.Targets.Contains(consoleTarget)).ToList();
        consoleRules.Any(r => r.Levels.Contains(LogLevel.Error)).Should().BeTrue();
    }
}
