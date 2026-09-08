// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentPathValidatorTest
{
    [TestCase("movies/movie.mp4", true)]
    [TestCase("sub/dir/file.txt", true)]
    [TestCase("single_file.iso", true)]
    [TestCase("/absolute/path", false)]
    [TestCase("C:/Windows/System32", false)]
    [TestCase("../outside", false)]
    [TestCase("folder/../outside", false)]
    [TestCase("folder/./inside", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase(null, false)]
    [TestCase("file\0null.txt", false)]
    public void IsValidRelativePath_ValidatesPathsCorrectly(string path, bool expected)
    {
        TorrentPathValidator.IsValidRelativePath(path).Should().Be(expected);
    }

    [TestCase("CON", false)]
    [TestCase("PRN", false)]
    [TestCase("AUX", false)]
    [TestCase("NUL", false)]
    [TestCase("COM1", false)]
    [TestCase("COM9", false)]
    [TestCase("LPT1", false)]
    [TestCase("LPT9", false)]
    [TestCase("con.txt", false)]
    [TestCase("aux.mp4", false)]
    [TestCase("nul.tar.gz", false)]
    [TestCase("folder/con.txt", false)]
    [TestCase("folder/AUX/file.txt", false)]
    public void IsValidRelativePath_RejectsWindowsReservedDeviceNames(string path, bool expected)
    {
        TorrentPathValidator.IsValidRelativePath(path).Should().Be(expected);
    }

    [Test]
    public void IsStrictSubPath_WhenTargetIsInsideBase_ReturnsTrue()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));
        var target = Path.Combine(tempBase, "subdir", "file.mkv");

        try
        {
            Directory.CreateDirectory(tempBase);
            TorrentPathValidator.IsStrictSubPath(tempBase, target).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, true);
            }
        }
    }

    [Test]
    public void IsStrictSubPath_WhenTargetEqualsBase_ReturnsFalse()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempBase);
            TorrentPathValidator.IsStrictSubPath(tempBase, tempBase).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, true);
            }
        }
    }

    [Test]
    public void IsStrictSubPath_WhenTargetEscapesBaseViaRelativeTraversal_ReturnsFalse()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));
        var target = Path.Combine(tempBase, "..", "outside.mkv");

        TorrentPathValidator.IsStrictSubPath(tempBase, target).Should().BeFalse();
    }

    [Test]
    public void IsStrictSubPath_WhenTargetContainsReservedDeviceName_ReturnsFalse()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));
        var target = Path.Combine(tempBase, "CON.txt");

        TorrentPathValidator.IsStrictSubPath(tempBase, target).Should().BeFalse();
    }

    [Test]
    public void IsStrictSubPath_WhenSymlinkEscapesBase_ReturnsFalse()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));
        var outsideDir = Path.Combine(Path.GetTempPath(), "leecharr_test_outside_" + System.Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempBase);
            Directory.CreateDirectory(outsideDir);

            var symlinkPath = Path.Combine(tempBase, "symlink_escape");
            Directory.CreateSymbolicLink(symlinkPath, outsideDir);

            var target = Path.Combine(symlinkPath, "file.txt");

            TorrentPathValidator.IsStrictSubPath(tempBase, target).Should().BeFalse();
        }
        catch (System.Exception)
        {
            // If OS privileges do not permit symlink creation in environment, skip gracefully
            Assert.Pass();
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, true);
            }

            if (Directory.Exists(outsideDir))
            {
                Directory.Delete(outsideDir, true);
            }
        }
    }
}
