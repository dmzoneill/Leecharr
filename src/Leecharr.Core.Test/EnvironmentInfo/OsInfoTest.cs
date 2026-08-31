// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Core.Test.EnvironmentInfo;

[TestFixture]
public class OsInfoTest
{
    private string originalContainerEnv;

    [SetUp]
    public void SetUp()
    {
        this.originalContainerEnv = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", this.originalContainerEnv);
    }

    [Test]
    public void PlatformProperties_MatchesRuntimeInformation()
    {
        OsInfo.IsWindows.Should().Be(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        OsInfo.IsLinux.Should().Be(RuntimeInformation.IsOSPlatform(OSPlatform.Linux));
        OsInfo.IsOsx.Should().Be(RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
    }

    [Test]
    public void PlatformProperties_ExactlyOneMajorOsIsTrue()
    {
        var activePlatforms = 0;
        if (OsInfo.IsWindows)
        {
            activePlatforms++;
        }

        if (OsInfo.IsLinux)
        {
            activePlatforms++;
        }

        if (OsInfo.IsOsx)
        {
            activePlatforms++;
        }

        activePlatforms.Should().Be(1);
    }

    [Test]
    public void Os_And_Version_ReturnsNonEmptyStrings()
    {
        OsInfo.Os.Should().NotBeNullOrWhiteSpace();
        OsInfo.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void IsContainer_WhenDotnetRunningInContainerIsTrue_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");

        OsInfo.IsContainer.Should().BeTrue();
        OsInfo.IsDocker.Should().BeTrue();
    }

    [Test]
    public void IsContainer_WhenDotnetRunningInContainerIsOne_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "1");

        OsInfo.IsContainer.Should().BeTrue();
        OsInfo.IsDocker.Should().BeTrue();
    }

    [Test]
    public void IsContainer_WhenDotnetRunningInContainerIsUppercaseTrue_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "TRUE");

        OsInfo.IsContainer.Should().BeTrue();
        OsInfo.IsDocker.Should().BeTrue();
    }

    [Test]
    public void IsContainer_WhenDotnetRunningInContainerIsFalse_ReturnsFalseIfNoDockerEnv()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "false");

        var hasDockerEnvFile = File.Exists("/.dockerenv");
        OsInfo.IsContainer.Should().Be(hasDockerEnvFile);
        OsInfo.IsDocker.Should().Be(hasDockerEnvFile);
    }

    [Test]
    public void IsContainer_WhenDotnetRunningInContainerIsNull_ReturnsFalseIfNoDockerEnv()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);

        var hasDockerEnvFile = File.Exists("/.dockerenv");
        OsInfo.IsContainer.Should().Be(hasDockerEnvFile);
        OsInfo.IsDocker.Should().Be(hasDockerEnvFile);
    }
}
