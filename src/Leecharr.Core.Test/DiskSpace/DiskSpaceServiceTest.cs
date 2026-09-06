// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;

namespace Leecharr.Core.Test.DiskSpace;

[TestFixture]
public class DiskSpaceServiceTest
{
    private IAppFolderInfo appFolderInfo = null!;
    private IDiskProvider diskProvider = null!;
    private IConfigService configService = null!;
    private DiskSpaceService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.service = new DiskSpaceService(this.appFolderInfo, this.configService, diskProvider: this.diskProvider);
    }

    [Test]
    public void GetDiskSpace_ReturnsDriveInfo_WhenAppFoldersAreProvided()
    {
        var tempPath = Path.GetTempPath();
        this.appFolderInfo.AppDataFolder.Returns(tempPath);
        this.appFolderInfo.StartUpFolder.Returns(tempPath);
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L);
        this.diskProvider.GetTotalSize(Arg.Any<string>()).Returns(500_000_000L);

        var result = this.service.GetDiskSpace();

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(info =>
        {
            info.Path.Should().NotBeNullOrEmpty();
            info.TotalSpace.Should().BeGreaterThan(0);
            info.FreeSpace.Should().BeGreaterThanOrEqualTo(0);
        });
    }

    [Test]
    public void GetDiskSpace_WhenAppFoldersAreNullOrEmpty_DoesNotThrowAndReturnsFixedDrives()
    {
        this.appFolderInfo.AppDataFolder.Returns((string)null!);
        this.appFolderInfo.StartUpFolder.Returns(string.Empty);

        var result = this.service.GetDiskSpace();

        result.Should().NotBeNull();
    }

    [Test]
    public void GetDiskSpace_DeduplicatesDrivesWithSameRoot()
    {
        var tempPath = Path.GetTempPath();
        this.appFolderInfo.AppDataFolder.Returns(tempPath);
        this.appFolderInfo.StartUpFolder.Returns(tempPath);
        this.diskProvider.GetAvailableSpace(tempPath).Returns(100_000_000L);
        this.diskProvider.GetTotalSize(tempPath).Returns(500_000_000L);

        var result = this.service.GetDiskSpace();

        var root = Path.GetPathRoot(tempPath);
        if (!string.IsNullOrEmpty(root))
        {
            result.FindAll(d => string.Equals(d.Path, root, StringComparison.OrdinalIgnoreCase)).Should().HaveCountLessThanOrEqualTo(1);
        }
    }

    [Test]
    public void GetDiskSpace_WhenPathIsInvalid_HandlesGracefullyWithoutThrowing()
    {
        this.appFolderInfo.AppDataFolder.Returns("invalid_drive_xyz:\\nonexistent\\path");
        this.appFolderInfo.StartUpFolder.Returns("another_invalid_path");
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns((long?)null);
        this.diskProvider.GetTotalSize(Arg.Any<string>()).Returns((long?)null);

        var result = this.service.GetDiskSpace();

        result.Should().NotBeNull();
    }

    [Test]
    public void GetDiskSpace_WhenSubdirectoriesResolvedViaDiskProvider_IncludesDownloadsAndAppData()
    {
        this.configService.DownloadDir.Returns("/downloads/torrents");
        this.appFolderInfo.AppDataFolder.Returns("/home/user/.config/Leecharr");
        this.appFolderInfo.StartUpFolder.Returns("/opt/leecharr");

        this.diskProvider.GetAvailableSpace("/downloads/torrents").Returns(50_000_000_000L);
        this.diskProvider.GetTotalSize("/downloads/torrents").Returns(100_000_000_000L);

        this.diskProvider.GetAvailableSpace("/home/user/.config/Leecharr").Returns(20_000_000_000L);
        this.diskProvider.GetTotalSize("/home/user/.config/Leecharr").Returns(50_000_000_000L);

        this.diskProvider.GetAvailableSpace("/opt/leecharr").Returns(10_000_000_000L);
        this.diskProvider.GetTotalSize("/opt/leecharr").Returns(30_000_000_000L);

        var result = this.service.GetDiskSpace();

        result.Should().Contain(d => d.Label == "Downloads" && d.Path == "/downloads/torrents" && d.FreeSpace == 50_000_000_000L);
        result.Should().Contain(d => d.Label == "AppData" && d.Path == "/home/user/.config/Leecharr" && d.FreeSpace == 20_000_000_000L);
        result.Should().Contain(d => d.Label == "Startup" && d.Path == "/opt/leecharr" && d.FreeSpace == 10_000_000_000L);
    }

    [Test]
    public void GetDiskSpace_WhenCategoriesConfigured_IncludesCategorySavePaths()
    {
        var categoryService = Substitute.For<NzbDrone.Core.Categories.ICategoryService>();
        var tempCategoryDir = Path.Combine(Path.GetTempPath(), "leecharr_test_cat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCategoryDir);

        try
        {
            var categories = new List<NzbDrone.Core.Categories.Category>
            {
                new() { Id = 1, Name = "Movies", SavePath = tempCategoryDir },
            };
            categoryService.GetAll().Returns(categories);

            this.diskProvider.GetAvailableSpace(tempCategoryDir).Returns(80_000_000_000L);
            this.diskProvider.GetTotalSize(tempCategoryDir).Returns(200_000_000_000L);

            var diskService = new DiskSpaceService(this.appFolderInfo, this.configService, this.diskProvider, categoryService);
            var result = diskService.GetDiskSpace();

            result.Should().Contain(d => d.Label == "Category: Movies" && d.Path == tempCategoryDir && d.FreeSpace == 80_000_000_000L);
        }
        finally
        {
            if (Directory.Exists(tempCategoryDir))
            {
                Directory.Delete(tempCategoryDir);
            }
        }
    }
}
