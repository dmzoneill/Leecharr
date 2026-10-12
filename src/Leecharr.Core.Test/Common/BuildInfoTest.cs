// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Core.Test.Common;

[TestFixture]
public class BuildInfoTest
{
    [Test]
    public void ParseVersionFileContent_PrefersVersionKeyOverEarlierDirectParse()
    {
        var content = "1\nversion=2.2.1\n";

        var version = BuildInfo.ParseVersionFileContent(content);

        version.Should().Be(new System.Version(2, 2, 1));
    }

    [Test]
    public void ParseVersionFileContent_ReturnsDirectVersionWhenNoVersionKey()
    {
        var content = "3.4.5\n";

        var version = BuildInfo.ParseVersionFileContent(content);

        version.Should().Be(new System.Version(3, 4, 5));
    }
}
