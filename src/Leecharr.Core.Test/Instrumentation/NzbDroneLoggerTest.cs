// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NLog;
using NLog.Targets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;

namespace Leecharr.Core.Test.Instrumentation;

[TestFixture]
public class NzbDroneLoggerTest
{
    private string tempAppDataFolder = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempAppDataFolder = Path.Combine(Path.GetTempPath(), "Leecharr_LoggerTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(this.tempAppDataFolder);
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
    public void Register_WithAppFolderInfo_ConfiguresFileTargetInLogsFolder()
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempAppDataFolder);

        NzbDroneLogger.Register(startupContext: null, appFolderInfo: appFolderInfo);

        var config = LogManager.Configuration;
        config.Should().NotBeNull();

        var fileTarget = config.FindTargetByName<FileTarget>("file");
        fileTarget.Should().NotBeNull();

        var expectedLogDir = Path.Combine(this.tempAppDataFolder, "logs");
        Directory.Exists(expectedLogDir).Should().BeTrue();
    }

    [Test]
    public void Register_WithDebugStartupFlag_ConfiguresDebugConsoleLevel()
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempAppDataFolder);
        var startupContext = new StartupContext(new[] { "-d" });

        NzbDroneLogger.Register(startupContext, appFolderInfo);

        var config = LogManager.Configuration;
        config.Should().NotBeNull();

        var consoleTarget = config.FindTargetByName<ColoredConsoleTarget>("console");
        consoleTarget.Should().NotBeNull();

        var consoleRules = config.LoggingRules.Where(r => r.Targets.Contains(consoleTarget)).ToList();
        consoleRules.Should().NotBeEmpty();
        consoleRules.Any(r => r.Levels.Contains(LogLevel.Debug)).Should().BeTrue();
    }

    private static IEnumerable<TestCaseData> RegisterLogLevelCases()
    {
        yield return new TestCaseData("error", LogLevel.Error);
        yield return new TestCaseData("warn", LogLevel.Warn);
        yield return new TestCaseData("off", LogLevel.Off);
    }

    [TestCaseSource(nameof(RegisterLogLevelCases))]
    public void Register_WithLogLevelArg_ConfiguresConsoleLevel(string levelArg, LogLevel expectedLevel)
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempAppDataFolder);
        var startupContext = new StartupContext(new[] { "--log-level=" + levelArg });

        NzbDroneLogger.Register(startupContext, appFolderInfo);

        var config = LogManager.Configuration;
        config.Should().NotBeNull();

        var consoleTarget = config.FindTargetByName<ColoredConsoleTarget>("console");
        consoleTarget.Should().NotBeNull();

        var consoleRules = config.LoggingRules.Where(r => r.Targets.Contains(consoleTarget)).ToList();
        consoleRules.Should().NotBeEmpty();
        consoleRules.Any(r => r.Levels.Contains(expectedLevel)).Should().BeTrue();
    }

    [Test]
    public void Reconfigure_UpdatesLoggingRules()
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempAppDataFolder);

        NzbDroneLogger.Register(startupContext: null, appFolderInfo: appFolderInfo);
        NzbDroneLogger.Reconfigure("Trace", "Trace");

        var config = LogManager.Configuration;
        config.Should().NotBeNull();

        var consoleTarget = config.FindTargetByName<ColoredConsoleTarget>("console");
        var consoleRules = config.LoggingRules.Where(r => r.Targets.Contains(consoleTarget)).ToList();
        consoleRules.Any(r => r.Levels.Contains(LogLevel.Trace)).Should().BeTrue();
    }
}
