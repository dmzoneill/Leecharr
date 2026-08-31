using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Core.Test.Common;

[TestFixture]
public class EnvironmentInfoTest
{
    [Test]
    public void StartupContext_ParsesArgsCorrectly()
    {
        var context = new StartupContext(new[] { "-nobrowser", "--data=/custom/data", "-v" });

        context.Flags.Should().Contain("nobrowser");
        context.Flags.Should().Contain("v");
        context.Args.Should().ContainKey("data");
        context.Args["data"].Should().Be("/custom/data");
    }

    [Test]
    public void OsInfo_ProvidesPlatformProperties()
    {
        (OsInfo.IsLinux ^ OsInfo.IsWindows ^ OsInfo.IsOsx).Should().BeTrue();
        OsInfo.Version.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void BuildInfo_ProvidesVersionInformation()
    {
        BuildInfo.Version.Should().NotBeNull();
    }
}
