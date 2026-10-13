// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class StoragePathServiceComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void StoragePath_DirectoriesAndWorkingPaths_CalculatesCorrectLocations()
    {
        var services = GlobalSetup.Factory.Services;
        var storagePathService = services.GetService(typeof(IStoragePathService)) as IStoragePathService;
        storagePathService.Should().NotBeNull();

        // 1. GetIncompleteDirectory
        var incompleteDir = storagePathService.GetIncompleteDirectory();
        incompleteDir.Should().NotBeNullOrWhiteSpace();

        // 2. GetCompletedDirectory with default vs category
        var defaultCompleted = storagePathService.GetCompletedDirectory(null);
        defaultCompleted.Should().NotBeNullOrWhiteSpace();

        var categoryCompleted = storagePathService.GetCompletedDirectory("tv");
        categoryCompleted.Should().NotBeNullOrWhiteSpace();

        // 3. GetFinalPath
        var finalPath = storagePathService.GetFinalPath("movies", "Inception.2010.1080p.mkv");
        finalPath.Should().NotBeNullOrWhiteSpace();
        finalPath.Should().Contain("Inception.2010.1080p.mkv");

        // 4. GetWorkingPath
        var workingPath = storagePathService.GetWorkingPath("1111222233334444555566667777888899990000", "SampleMovie.mkv", "movies");
        workingPath.Should().NotBeNullOrWhiteSpace();
        workingPath.Should().Contain("SampleMovie.mkv");
    }

    [Test]
    public void StoragePath_MoveToCompleted_RelocatesFileSuccessfully()
    {
        var services = GlobalSetup.Factory.Services;
        var storagePathService = services.GetService(typeof(IStoragePathService)) as IStoragePathService;
        storagePathService.Should().NotBeNull();

        var tempDir = Path.Combine(Path.GetTempPath(), $"storage_src_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceFile = Path.Combine(tempDir, "storage_test_item.mkv");
        File.WriteAllText(sourceFile, "dummy video data content");

        string finalDestination = null;
        try
        {
            var moved = storagePathService.MoveToCompleted(sourceFile, "test-cat", "storage_test_item.mkv", out finalDestination);
            moved.Should().BeTrue();
            finalDestination.Should().NotBeNullOrWhiteSpace();
            File.Exists(finalDestination).Should().BeTrue();
            File.Exists(sourceFile).Should().BeFalse();
        }
        finally
        {
            if (!string.IsNullOrEmpty(finalDestination) && File.Exists(finalDestination))
            {
                File.Delete(finalDestination);
            }

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void StoragePath_StripIncompleteExtensions_RenamesFilesCleanly()
    {
        var services = GlobalSetup.Factory.Services;
        var storagePathService = services.GetService(typeof(IStoragePathService)) as IStoragePathService;
        storagePathService.Should().NotBeNull();

        var tempDir = Path.Combine(Path.GetTempPath(), $"strip_ext_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var file1 = Path.Combine(tempDir, "video.mkv.!mt");
        var file2 = Path.Combine(tempDir, "audio.flac.!leech");
        var file3 = Path.Combine(tempDir, "sample.avi.incomplete");
        File.WriteAllText(file1, "data1");
        File.WriteAllText(file2, "data2");
        File.WriteAllText(file3, "data3");

        try
        {
            storagePathService.StripIncompleteExtensions(tempDir);

            File.Exists(Path.Combine(tempDir, "video.mkv")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "audio.flac")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "sample.avi")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void StoragePath_StripIncompleteExtensions_DoesNotOverwriteExistingCleanFile()
    {
        var services = GlobalSetup.Factory.Services;
        var storagePathService = services.GetService(typeof(IStoragePathService)) as IStoragePathService;
        storagePathService.Should().NotBeNull();

        var tempDir = Path.Combine(Path.GetTempPath(), $"strip_collision_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var clean = Path.Combine(tempDir, "video.mkv");
        var partial = Path.Combine(tempDir, "video.mkv.!mt");
        var renamed = Path.Combine(tempDir, "video_1.mkv");
        File.WriteAllText(clean, "complete");
        File.WriteAllText(partial, "partial");

        try
        {
            storagePathService.StripIncompleteExtensions(tempDir);

            File.ReadAllText(clean).Should().Be("complete");
            File.Exists(partial).Should().BeFalse();
            File.ReadAllText(renamed).Should().Be("partial");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void StoragePath_StripIncompleteExtensions_DoesNotRenameFilesThroughDirectorySymlink()
    {
        var services = GlobalSetup.Factory.Services;
        var storagePathService = services.GetService(typeof(IStoragePathService)) as IStoragePathService;
        storagePathService.Should().NotBeNull();

        var tempDir = Path.Combine(Path.GetTempPath(), $"strip_link_{Guid.NewGuid():N}");
        var outsideDir = Path.Combine(Path.GetTempPath(), $"strip_outside_{Guid.NewGuid():N}");
        var link = Path.Combine(tempDir, "linked");
        var outsideFile = Path.Combine(outsideDir, "video.mkv.!mt");

        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(outsideDir);
            File.WriteAllText(outsideFile, "outside");
            Directory.CreateSymbolicLink(link, outsideDir);

            storagePathService.StripIncompleteExtensions(tempDir);

            File.Exists(outsideFile).Should().BeTrue();
            File.ReadAllText(outsideFile).Should().Be("outside");
            File.Exists(Path.Combine(outsideDir, "video.mkv")).Should().BeFalse();
        }
        catch (IOException)
        {
            Assert.Pass();
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Pass();
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Pass();
        }
        finally
        {
            try
            {
                if (File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint))
                {
                    File.Delete(link);
                }
            }
            catch (Exception)
            {
                // The link may not exist when symlink creation is unavailable.
            }

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception)
            {
                // Cleanup must not hide the test result.
            }

            try
            {
                if (Directory.Exists(outsideDir))
                {
                    Directory.Delete(outsideDir, true);
                }
            }
            catch (Exception)
            {
                // Cleanup must not hide the test result.
            }
        }
    }

    [Test]
    public void StoragePath_NormalizeCompletedSavePath_HandlesPaths()
    {
        var services = GlobalSetup.Factory.Services;
        var storagePathService = services.GetService(typeof(IStoragePathService)) as IStoragePathService;
        storagePathService.Should().NotBeNull();

        var normalized = storagePathService.NormalizeCompletedSavePath("/downloads/movies/subfolder", "movies");
        normalized.Should().NotBeNullOrWhiteSpace();

        var nullNormalized = storagePathService.NormalizeCompletedSavePath(string.Empty, null);
        nullNormalized.Should().NotBeNullOrWhiteSpace();
    }
}
