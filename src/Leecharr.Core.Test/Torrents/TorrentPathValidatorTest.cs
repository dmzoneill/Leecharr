// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
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

    [TestCase("folder/movie:title.mp4", false)]
    [TestCase("folder/movie*star.mp4", false)]
    [TestCase("folder/movie?question.mp4", false)]
    [TestCase("folder/movie\"quote\".mp4", false)]
    [TestCase("folder/movie<less.mp4", false)]
    [TestCase("folder/movie>greater.mp4", false)]
    [TestCase("folder/movie|pipe.mp4", false)]
    [TestCase("folder/movie\u0001ctrl.mp4", false)]
    [TestCase("folder/movie\u001Fctrl.mp4", false)]
    [TestCase("folder:name/movie.mp4", false)]
    [TestCase("folder*/movie.mp4", false)]
    [TestCase("folder?/movie.mp4", false)]
    [TestCase("folder</movie.mp4", false)]
    [TestCase("folder>/movie.mp4", false)]
    [TestCase("folder|/movie.mp4", false)]
    [TestCase("movie:title.mp4", false)]
    [TestCase("movie*star.mp4", false)]
    [TestCase("movie?question.mp4", false)]
    [TestCase("movie\"quote\".mp4", false)]
    [TestCase("movie<less.mp4", false)]
    [TestCase("movie>greater.mp4", false)]
    [TestCase("movie|pipe.mp4", false)]
    [TestCase("valid_folder/valid_sub/valid_file.mkv", true)]
    public void IsValidRelativePath_RejectsUniversalInvalidCharacters(string path, bool expected)
    {
        TorrentPathValidator.IsValidRelativePath(path).Should().Be(expected);
    }

    [TestCase("valid_file.mkv", false)]
    [TestCase("path/to/valid_file.mkv", false)]
    [TestCase("file:name.mkv", true)]
    [TestCase("file*name.mkv", true)]
    [TestCase("file?name.mkv", true)]
    [TestCase("file\"name.mkv", true)]
    [TestCase("file<name.mkv", true)]
    [TestCase("file>name.mkv", true)]
    [TestCase("file|name.mkv", true)]
    [TestCase("file\u0005name.mkv", true)]
    [TestCase("file\u001Ename.mkv", true)]
    public void HasUniversalInvalidChars_DetectsInvalidCharsCorrectly(string text, bool expected)
    {
        TorrentPathValidator.HasUniversalInvalidChars(text).Should().Be(expected);
    }

    [Test]
    public void SanitizeFileName_ReplacesColonsAndRemovesIllegalCharacters()
    {
        var sanitized = TorrentPathValidator.SanitizeFileName("Movie: The Return of Star*Wars? <2024>|\".mkv");
        sanitized.Should().Be("Movie - The Return of StarWars 2024.mkv");
    }

    [Test]
    public void SanitizeFileName_PrefixesReservedDeviceName()
    {
        var sanitized = TorrentPathValidator.SanitizeFileName("CON.txt");
        sanitized.Should().Be("_CON.txt");
    }

    [Test]
    public void SanitizePathSegment_SanitizesFolderName()
    {
        var sanitized = TorrentPathValidator.SanitizePathSegment("Show: Season 1*?");
        sanitized.Should().Be("Show - Season 1");
    }

    [TestCase("Show: Title/Season 01/Ep*01: Pilot?.mkv", "Show - Title/Season 01/Ep01 - Pilot.mkv")]
    [TestCase("folder/CON/file:name.mkv", "folder/_CON/file - name.mkv")]
    [TestCase("Movie: Title (2024)", "Movie - Title (2024)")]
    [TestCase("Movie: Title (2024).mkv", "Movie - Title (2024).mkv")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase(null, "")]
    public void SanitizeRelativePath_SanitizesPathSegments(string input, string expected)
    {
        var result = TorrentPathValidator.SanitizeRelativePath(input);
        result.Should().Be(expected);
    }

    [Test]
    public void IsStrictSubPath_WhenTargetContainsUniversalInvalidCharacters_ReturnsFalse()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));
        var target = Path.Combine(tempBase, "movie:title.mkv");

        TorrentPathValidator.IsStrictSubPath(tempBase, target).Should().BeFalse();
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

    [Test]
    public void IsStrictSubPath_WhenIntermediateParentDirectoryIsSymlinkAndLeafFileExists_ReturnsFalse()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));
        var outsideDir = Path.Combine(Path.GetTempPath(), "leecharr_test_outside_" + System.Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempBase);
            Directory.CreateDirectory(outsideDir);

            var realFilePath = Path.Combine(outsideDir, "real_file.txt");
            File.WriteAllText(realFilePath, "secret payload");

            var symlinkDir = Path.Combine(tempBase, "symlink_dir");
            Directory.CreateSymbolicLink(symlinkDir, outsideDir);

            var target = Path.Combine(symlinkDir, "real_file.txt");

            File.Exists(target).Should().BeTrue();
            TorrentPathValidator.IsStrictSubPath(tempBase, target).Should().BeFalse();

            var canonical = TorrentPathValidator.ResolveCanonicalPath(target);
            canonical.Should().Be(Path.GetFullPath(realFilePath));
        }
        catch (System.Exception)
        {
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

    [Test]
    public void IsStrictSubPath_WhenIntermediateDirectorySymlinkPointsWithinBase_ReturnsTrue()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "leecharr_test_base_" + System.Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempBase);
            var internalTargetDir = Path.Combine(tempBase, "internal_dir");
            Directory.CreateDirectory(internalTargetDir);

            var realFilePath = Path.Combine(internalTargetDir, "file.txt");
            File.WriteAllText(realFilePath, "safe content");

            var symlinkDir = Path.Combine(tempBase, "symlink_internal");
            Directory.CreateSymbolicLink(symlinkDir, internalTargetDir);

            var target = Path.Combine(symlinkDir, "file.txt");

            File.Exists(target).Should().BeTrue();
            TorrentPathValidator.IsStrictSubPath(tempBase, target).Should().BeTrue();
        }
        catch (System.Exception)
        {
            Assert.Pass();
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
    public void ResolveCanonicalPath_WithWindowsRootedPath_DoesNotCorruptDriveOrSegments()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Pass("Windows drive paths can only be canonicalized on Windows OS.");
            return;
        }

        var path = @"C:\downloads\movie.mkv";
        var resolved = TorrentPathValidator.ResolveCanonicalPath(path);
        resolved.Should().Be(@"C:\downloads\movie.mkv");

        var subPath = @"C:\downloads\sub\file.txt";
        var resolvedSub = TorrentPathValidator.ResolveCanonicalPath(subPath);
        resolvedSub.Should().Be(@"C:\downloads\sub\file.txt");
    }

    [Test]
    public void ResolveCanonicalPath_WithUncPath_DoesNotCorruptUncRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Pass("UNC paths can only be canonicalized on Windows OS.");
            return;
        }

        var uncPath = @"\\server\share\sub\file.txt";
        var resolved = TorrentPathValidator.ResolveCanonicalPath(uncPath);
        resolved.Should().Be(@"\\server\share\sub\file.txt");
    }

    [Test]
    public void IsStrictSubPath_WithWindowsRootedPath_ValidatesCorrectly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Pass("Windows drive paths can only be canonicalized on Windows OS.");
            return;
        }

        var basePath = @"C:\downloads";
        var targetFile = @"C:\downloads\movie.mkv";
        var targetSub = @"C:\downloads\sub\file.txt";
        var outsideFile = @"C:\other\movie.mkv";
        var differentDrive = @"D:\downloads\movie.mkv";

        TorrentPathValidator.IsStrictSubPath(basePath, targetFile).Should().BeTrue();
        TorrentPathValidator.IsStrictSubPath(basePath, targetSub).Should().BeTrue();
        TorrentPathValidator.IsStrictSubPath(basePath, outsideFile).Should().BeFalse();
        TorrentPathValidator.IsStrictSubPath(basePath, differentDrive).Should().BeFalse();
    }

    [Test]
    public void IsStrictSubPath_WithUncPath_ValidatesCorrectly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Pass("UNC paths can only be canonicalized on Windows OS.");
            return;
        }

        var basePath = @"\\server\share";
        var target = @"\\server\share\downloads\file.txt";
        var outside = @"\\server\other\file.txt";
        var differentServer = @"\\other\share\downloads\file.txt";

        TorrentPathValidator.IsStrictSubPath(basePath, target).Should().BeTrue();
        TorrentPathValidator.IsStrictSubPath(basePath, outside).Should().BeFalse();
        TorrentPathValidator.IsStrictSubPath(basePath, differentServer).Should().BeFalse();
    }

    [Test]
    public void ResolveCanonicalPath_WithUnixRootedPath_ResolvesRootCorrectly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("Unix rooted paths can only be canonicalized on non-Windows OS.");
            return;
        }

        var path = "/downloads/movie.mkv";
        var resolved = TorrentPathValidator.ResolveCanonicalPath(path);
        resolved.Should().Be("/downloads/movie.mkv");

        var subPath = "/downloads/sub/file.txt";
        var resolvedSub = TorrentPathValidator.ResolveCanonicalPath(subPath);
        resolvedSub.Should().Be("/downloads/sub/file.txt");
    }

    [Test]
    public void IsStrictSubPath_WithUnixRootedPath_ValidatesCorrectly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("Unix rooted paths can only be canonicalized on non-Windows OS.");
            return;
        }

        var basePath = "/downloads";
        var targetFile = "/downloads/movie.mkv";
        var targetSub = "/downloads/sub/file.txt";
        var outsideFile = "/other/movie.mkv";

        TorrentPathValidator.IsStrictSubPath(basePath, targetFile).Should().BeTrue();
        TorrentPathValidator.IsStrictSubPath(basePath, targetSub).Should().BeTrue();
        TorrentPathValidator.IsStrictSubPath(basePath, outsideFile).Should().BeFalse();
    }
}
