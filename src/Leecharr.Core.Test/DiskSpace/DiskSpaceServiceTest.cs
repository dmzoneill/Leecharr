// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

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

    [Test]
    public void GetDiskSpace_WhenIncompleteDownloadDirConfigured_IncludesIncompleteDownloads()
    {
        this.configService.DownloadDir.Returns("/downloads/torrents");
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");

        this.diskProvider.GetAvailableSpace("/downloads/torrents").Returns(50_000_000_000L);
        this.diskProvider.GetTotalSize("/downloads/torrents").Returns(100_000_000_000L);

        this.diskProvider.GetAvailableSpace("/downloads/incomplete").Returns(30_000_000_000L);
        this.diskProvider.GetTotalSize("/downloads/incomplete").Returns(80_000_000_000L);

        var result = this.service.GetDiskSpace();

        result.Should().Contain(d => d.Label == "Downloads" && d.Path == "/downloads/torrents" && d.FreeSpace == 50_000_000_000L);
        result.Should().Contain(d => d.Label == "Incomplete Downloads" && d.Path == "/downloads/incomplete" && d.FreeSpace == 30_000_000_000L);
    }

    [Test]
    public void GetDiskSpace_DeduplicatesSharedPhysicalVolumes()
    {
        this.configService.DownloadDir.Returns("/downloads");
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.appFolderInfo.AppDataFolder.Returns("/downloads/appdata");

        // Simulate all three paths sharing the same physical drive / mount
        this.diskProvider.GetAvailableSpace(Arg.Is<string>(s => s.StartsWith("/downloads"))).Returns(50_000_000_000L);
        this.diskProvider.GetTotalSize(Arg.Is<string>(s => s.StartsWith("/downloads"))).Returns(100_000_000_000L);

        var result = this.service.GetDiskSpace();

        // Should only contain 1 entry for /downloads, not 3 duplicate entries
        result.Where(d => d.Path.StartsWith("/downloads")).Should().HaveCount(1);
    }

    [Test]
    public void GetDiskSpace_DoesNotPublishAnyEventsOnReadQuery()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        this.configService.LowDiskSpaceThresholdMb.Returns(500);
        this.configService.DownloadDir.Returns("/downloads");

        // 100 MB free (well below warning and critical)
        this.diskProvider.GetAvailableSpace("/downloads").Returns(100L * 1024 * 1024);
        this.diskProvider.GetTotalSize("/downloads").Returns(100L * 1024 * 1024 * 1024);

        var diskService = new DiskSpaceService(this.appFolderInfo, this.configService, this.diskProvider, eventAggregator: eventAggregator);
        var result = diskService.GetDiskSpace();

        result.Should().NotBeEmpty();
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceLowEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceCriticalEvent>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CheckDiskSpaceThresholds_WhenLowDiskSpaceThresholdMbIsZeroOrNegative_DefaultsToFiveGigabytes(int thresholdMb)
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        this.configService.LowDiskSpaceThresholdMb.Returns(thresholdMb);
        this.configService.DownloadDir.Returns("/downloads");

        // 2 GB free (below 5 GB default warning, above 1 GB default critical)
        this.diskProvider.GetAvailableSpace("/downloads").Returns(2L * 1024 * 1024 * 1024);
        this.diskProvider.GetTotalSize("/downloads").Returns(100L * 1024 * 1024 * 1024);

        var diskService = new DiskSpaceService(this.appFolderInfo, this.configService, this.diskProvider, eventAggregator: eventAggregator);
        diskService.CheckDiskSpaceThresholds();

        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceLowEvent>(e => e.DrivePath == "/downloads"));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e => e.DrivePath == "/downloads"));
    }

    [Test]
    public void CheckDiskSpaceThresholds_WhenDefaultThresholdsAndFreeSpaceBetweenCriticalAndWarning_PublishesLowDiskSpaceEventAndNotCritical()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        this.configService.LowDiskSpaceThresholdMb.Returns(0);
        this.configService.DownloadDir.Returns("/downloads");

        // 2 GB free space: default warning threshold is 5 GB, critical threshold is 1 GB
        this.diskProvider.GetAvailableSpace("/downloads").Returns(2L * 1024 * 1024 * 1024);
        this.diskProvider.GetTotalSize("/downloads").Returns(100L * 1024 * 1024 * 1024);

        var diskService = new DiskSpaceService(this.appFolderInfo, this.configService, this.diskProvider, eventAggregator: eventAggregator);
        diskService.CheckDiskSpaceThresholds();

        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceLowEvent>(e => e.DrivePath == "/downloads"));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e => e.DrivePath == "/downloads"));
    }

    [Test]
    public void CheckDiskSpaceThresholds_WhenDefaultThresholdsAndFreeSpaceBelowCritical_PublishesCriticalEventAndNotLowEvent()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        this.configService.LowDiskSpaceThresholdMb.Returns(0);
        this.configService.DownloadDir.Returns("/downloads");

        // 500 MB free space: below 1 GB default critical threshold
        this.diskProvider.GetAvailableSpace("/downloads").Returns(500L * 1024 * 1024);
        this.diskProvider.GetTotalSize("/downloads").Returns(100L * 1024 * 1024 * 1024);

        var diskService = new DiskSpaceService(this.appFolderInfo, this.configService, this.diskProvider, eventAggregator: eventAggregator);
        diskService.CheckDiskSpaceThresholds();

        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e => e.DrivePath == "/downloads"));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Is<DiskSpaceLowEvent>(e => e.DrivePath == "/downloads"));
    }

    [Test]
    public void CheckDiskSpaceThresholds_WhenCustomLowThresholdConfigured_ScalesCriticalThreshold()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        this.configService.LowDiskSpaceThresholdMb.Returns(500);
        this.configService.DownloadDir.Returns("/downloads");

        // 350 MB free space: warning threshold is 500 MB, critical threshold is 250 MB
        this.diskProvider.GetAvailableSpace("/downloads").Returns(350L * 1024 * 1024);
        this.diskProvider.GetTotalSize("/downloads").Returns(100L * 1024 * 1024 * 1024);

        var diskService = new DiskSpaceService(this.appFolderInfo, this.configService, this.diskProvider, eventAggregator: eventAggregator);
        diskService.CheckDiskSpaceThresholds();

        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceLowEvent>(e => e.DrivePath == "/downloads"));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e => e.DrivePath == "/downloads"));
    }

    [Test]
    public void CheckDiskSpaceThresholds_WhenAdequateFreeSpace_DoesNotPublishAnyEvents()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        this.configService.LowDiskSpaceThresholdMb.Returns(0);
        this.configService.DownloadDir.Returns("/downloads");

        // 10 GB free space on 100 GB drive (10% free, well above 5 GB default warning and 5% ratio)
        this.diskProvider.GetAvailableSpace("/downloads").Returns(10L * 1024 * 1024 * 1024);
        this.diskProvider.GetTotalSize("/downloads").Returns(100L * 1024 * 1024 * 1024);

        var diskService = new DiskSpaceService(this.appFolderInfo, this.configService, this.diskProvider, eventAggregator: eventAggregator);
        diskService.CheckDiskSpaceThresholds();

        eventAggregator.DidNotReceive().PublishEvent(Arg.Is<DiskSpaceLowEvent>(e => e.DrivePath == "/downloads"));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e => e.DrivePath == "/downloads"));
    }
}
