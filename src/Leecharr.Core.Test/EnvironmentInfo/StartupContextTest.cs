// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Core.Test.EnvironmentInfo;

[TestFixture]
public class StartupContextTest
{
    [Test]
    public void Constructor_WhenArgsNull_InitializesEmptyFlagsAndArgs()
    {
        var context = new StartupContext(null!);

        context.Flags.Should().NotBeNull();
        context.Flags.Should().BeEmpty();
        context.Args.Should().NotBeNull();
        context.Args.Should().BeEmpty();
    }

    [Test]
    public void Constructor_WhenArgsEmpty_InitializesEmptyFlagsAndArgs()
    {
        var context = new StartupContext();

        context.Flags.Should().BeEmpty();
        context.Args.Should().BeEmpty();
    }

    [Test]
    public void Constructor_WhenArgsContainWhitespaceOrEmpty_IgnoresThem()
    {
        var context = new StartupContext(string.Empty, "   ", "\t", "\n");

        context.Flags.Should().BeEmpty();
        context.Args.Should().BeEmpty();
    }

    [Test]
    public void Constructor_WhenPosixPathArgumentPassed_PreservesLeadingSlashesWithoutStripping()
    {
        var context = new StartupContext("/config/app", "/data/media/downloads");

        context.Flags.Should().Contain("/config/app");
        context.Flags.Should().Contain("/data/media/downloads");
        context.Flags.Should().NotContain("config/app");
    }

    [Test]
    public void Constructor_WhenPosixPathWithKeyValuePassed_PreservesKeyPathAndExtractsValue()
    {
        var context = new StartupContext("/config/app=/var/lib/leecharr");

        context.Args.Should().ContainKey("/config/app");
        context.Args["/config/app"].Should().Be("/var/lib/leecharr");
    }

    [Test]
    public void Constructor_WhenStandardSingleDashFlagPassed_ParsesFlagCorrectly()
    {
        var context = new StartupContext("-nobrowser", "-v", "-debug");

        context.Flags.Should().Contain("nobrowser");
        context.Flags.Should().Contain("v");
        context.Flags.Should().Contain("debug");
    }

    [Test]
    public void Constructor_WhenStandardDoubleDashFlagPassed_ParsesFlagCorrectly()
    {
        var context = new StartupContext("--nobrowser", "--version", "--help");

        context.Flags.Should().Contain("nobrowser");
        context.Flags.Should().Contain("version");
        context.Flags.Should().Contain("help");
    }

    [Test]
    public void Constructor_WhenDoubleDashKeyValuePassed_ExtractsToArgsDictionary()
    {
        var context = new StartupContext("--data=/custom/app/data", "--port=7889");

        context.Args.Should().ContainKey("data");
        context.Args["data"].Should().Be("/custom/app/data");
        context.Args.Should().ContainKey("port");
        context.Args["port"].Should().Be("7889");
    }

    [Test]
    public void Constructor_WhenSingleDashKeyValuePassed_ExtractsToArgsDictionary()
    {
        var context = new StartupContext("-data=/custom/data", "-bind=0.0.0.0");

        context.Args.Should().ContainKey("data");
        context.Args["data"].Should().Be("/custom/data");
        context.Args.Should().ContainKey("bind");
        context.Args["bind"].Should().Be("0.0.0.0");
    }

    [Test]
    public void Constructor_WhenKeysHaveMixedCase_AllowsCaseInsensitiveAccess()
    {
        var context = new StartupContext("--DATA=/custom/path", "--Log-Level=Debug");

        context.Args.Should().ContainKey("data");
        context.Args.Should().ContainKey("DATA");
        context.Args.Should().ContainKey("Data");
        context.Args["data"].Should().Be("/custom/path");
        context.Args["Log-Level"].Should().Be("Debug");
    }

    [Test]
    public void Constructor_WhenValueContainsEqualSigns_SplitsOnlyOnFirstEqual()
    {
        var context = new StartupContext("--url=http://localhost:8080/api?foo=bar&baz=qux");

        context.Args.Should().ContainKey("url");
        context.Args["url"].Should().Be("http://localhost:8080/api?foo=bar&baz=qux");
    }

    [Test]
    public void Constructor_WhenMixedFlagsAndArgsPassed_PopulatesBothCollections()
    {
        var context = new StartupContext(
            "-nobrowser",
            "--data=/custom/data",
            "/config/app",
            "-v",
            "--api-key=secret_token_123");

        context.Flags.Should().HaveCount(3);
        context.Flags.Should().Contain("nobrowser");
        context.Flags.Should().Contain("/config/app");
        context.Flags.Should().Contain("v");

        context.Args.Should().HaveCount(2);
        context.Args["data"].Should().Be("/custom/data");
        context.Args["api-key"].Should().Be("secret_token_123");
    }
}
