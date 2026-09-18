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
    private string originalContainer;
    private string originalK8sHost;

    [SetUp]
    public void SetUp()
    {
        this.originalContainerEnv = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        this.originalContainer = Environment.GetEnvironmentVariable("container");
        this.originalK8sHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");

        Environment.SetEnvironmentVariable("container", null);
        Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", this.originalContainerEnv);
        Environment.SetEnvironmentVariable("container", this.originalContainer);
        Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", this.originalK8sHost);
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
    public void IsContainer_WhenContainerEnvVarIsSet_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("container", "podman");

        OsInfo.IsContainer.Should().BeTrue();
        OsInfo.IsDocker.Should().BeTrue();
    }

    [Test]
    public void IsContainer_WhenKubernetesServiceHostIsSet_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "10.96.0.1");

        OsInfo.IsContainer.Should().BeTrue();
        OsInfo.IsDocker.Should().BeTrue();
    }

    [TestCase("12:memory:/docker/1234567890abcdef", true)]
    [TestCase("1:name=systemd:/kubepods.slice/kubepods-burstable.slice/pod123", true)]
    [TestCase("0::/system.slice/containerd.service", true)]
    [TestCase("1:memory:/lxc/my-container", true)]
    [TestCase("0::/libpod_parent/libpod-12345678", true)]
    [TestCase("0::/machine.slice/podman-1234.scope", true)]
    [TestCase("0::/init.scope", false)]
    [TestCase("0::/user.slice/user-1000.slice/session-1.scope", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void CheckCgroupContent_DetectsContainerSignatures(string cgroupContent, bool expected)
    {
        OsInfo.CheckCgroupContent(cgroupContent).Should().Be(expected);
    }

    [Test]
    public void IsContainer_WhenDotnetRunningInContainerIsFalse_ReturnsFalseIfNoContainerIndicators()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "false");

        var hasContainerIndicators = File.Exists("/.dockerenv") ||
                                     File.Exists("/run/.containerenv") ||
                                     File.Exists("/run/systemd/container") ||
                                     OsInfo.CheckCgroups();
        OsInfo.IsContainer.Should().Be(hasContainerIndicators);
        OsInfo.IsDocker.Should().Be(hasContainerIndicators);
    }

    [Test]
    public void IsContainer_WhenDotnetRunningInContainerIsNull_ReturnsFalseIfNoContainerIndicators()
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);

        var hasContainerIndicators = File.Exists("/.dockerenv") ||
                                     File.Exists("/run/.containerenv") ||
                                     File.Exists("/run/systemd/container") ||
                                     OsInfo.CheckCgroups();
        OsInfo.IsContainer.Should().Be(hasContainerIndicators);
        OsInfo.IsDocker.Should().Be(hasContainerIndicators);
    }
}
