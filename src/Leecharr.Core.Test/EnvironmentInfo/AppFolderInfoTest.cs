// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Core.Test.EnvironmentInfo;

[TestFixture]
public class AppFolderInfoTest
{
    private string originalAppDataEnv;
    private string originalXdgEnv;

    [SetUp]
    public void SetUp()
    {
        this.originalAppDataEnv = Environment.GetEnvironmentVariable("LEECHARR__APP_DATA");
        this.originalXdgEnv = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("LEECHARR__APP_DATA", this.originalAppDataEnv);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", this.originalXdgEnv);
    }

    [Test]
    public void Constructor_WhenLeecharrAppDataEnvSet_OverridesCliAndDefaultPaths()
    {
        var customEnvPath = Path.Combine(Path.GetTempPath(), "leecharr-env-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("LEECHARR__APP_DATA", customEnvPath);

        var context = new StartupContext("--data=/different/cli/path");
        var appFolderInfo = new AppFolderInfo(context);

        appFolderInfo.AppDataFolder.Should().Be(customEnvPath);
    }

    [Test]
    public void Constructor_WhenLeecharrAppDataEnvSetWithoutCli_UsesEnvVar()
    {
        var customEnvPath = Path.Combine(Path.GetTempPath(), "leecharr-env-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("LEECHARR__APP_DATA", customEnvPath);

        var context = new StartupContext();
        var appFolderInfo = new AppFolderInfo(context);

        appFolderInfo.AppDataFolder.Should().Be(customEnvPath);
    }

    [Test]
    public void Constructor_WhenNoEnvAndCliDataArgProvided_UsesCliArgument()
    {
        Environment.SetEnvironmentVariable("LEECHARR__APP_DATA", null);

        var cliPath = Path.Combine(Path.GetTempPath(), "leecharr-cli-" + Guid.NewGuid().ToString("N"));
        var context = new StartupContext($"--data={cliPath}");
        var appFolderInfo = new AppFolderInfo(context);

        appFolderInfo.AppDataFolder.Should().Be(cliPath);
    }

    [Test]
    public void Constructor_WhenNoEnvAndNoCli_OnLinuxWithXdgConfigHome_UsesXdgConfigPath()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
        {
            Assert.Ignore("Test is applicable only to Linux and FreeBSD");
            return;
        }

        Environment.SetEnvironmentVariable("LEECHARR__APP_DATA", null);
        var customXdg = Path.Combine(Path.GetTempPath(), "fake-xdg-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", customXdg);

        var context = new StartupContext();
        var appFolderInfo = new AppFolderInfo(context);

        appFolderInfo.AppDataFolder.Should().Be(Path.Combine(customXdg, "Leecharr"));
    }

    [Test]
    public void Constructor_WhenNoEnvAndNoCliAndNoXdg_OnLinux_FallsBackToUserHomeConfig()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
        {
            Assert.Ignore("Test is applicable only to Linux and FreeBSD");
            return;
        }

        Environment.SetEnvironmentVariable("LEECHARR__APP_DATA", null);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);

        var context = new StartupContext();
        var appFolderInfo = new AppFolderInfo(context);

        appFolderInfo.AppDataFolder.Should().EndWith(Path.Combine(".config", "Leecharr"));
    }

    [Test]
    public void Constructor_WhenStartupContextIsNull_InitializesCorrectly()
    {
        Environment.SetEnvironmentVariable("LEECHARR__APP_DATA", null);

        var appFolderInfo = new AppFolderInfo(null);

        appFolderInfo.AppDataFolder.Should().NotBeNullOrWhiteSpace();
        appFolderInfo.StartUpFolder.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void StartUpFolder_ReturnsAppDomainBaseDirectory()
    {
        var appFolderInfo = new AppFolderInfo(new StartupContext());

        appFolderInfo.StartUpFolder.Should().Be(AppDomain.CurrentDomain.BaseDirectory);
    }
}
