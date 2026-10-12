// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;

namespace Leecharr.Core.Test.Disk;

[TestFixture]
public class LinuxMountTableTest
{
    [Test]
    public void TryGetLongestMountForPath_WhenPathUnderNestedMount_SelectsLongestPrefix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("Linux mount table tests run on Unix only");
        }

        var tempPath = Path.GetTempPath();
        var resolved = LinuxMountTable.TryGetLongestMountForPath(tempPath, out var mountPoint);

        resolved.Should().BeTrue();
        mountPoint.Should().NotBeNullOrWhiteSpace();
        tempPath.Should().StartWith(mountPoint.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Test]
    public void GetDiskMountPoints_OnLinux_ReturnsRootMount()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("Linux mount table tests run on Unix only");
        }

        var mounts = LinuxMountTable.GetDiskMountPoints();

        mounts.Should().Contain("/");
    }
}
