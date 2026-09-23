// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Leecharr.Api.V1.DiskSpace;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DiskSpace;

namespace Leecharr.Core.Test.DiskSpace;

[TestFixture]
public class DiskSpaceControllerTest
{
    private IDiskSpaceService diskSpaceService = null!;
    private DiskSpaceController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.diskSpaceService = Substitute.For<IDiskSpaceService>();
        this.controller = new DiskSpaceController(this.diskSpaceService);
    }

    [Test]
    public void GetDiskSpace_WhenServiceReturnsEmptyList_ReturnsOkWithEmptyList()
    {
        this.diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>());

        var result = this.controller.GetDiskSpace();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var resources = okResult.Value as List<DiskSpaceResource>;

        resources.Should().NotBeNull();
        resources.Should().BeEmpty();
    }

    [Test]
    public void GetDiskSpace_MapsAllDrivePropertiesCorrectly_WithOneBasedIndices()
    {
        var driveInfos = new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/downloads",
                Label = "Downloads Volume",
                FreeSpace = 250_000_000_000L,
                TotalSpace = 1_000_000_000_000L,
                FileSystemType = "ext4",
                IsReadOnly = false,
            },
            new DiskSpaceInfo
            {
                Path = "/mnt/archive",
                Label = "Archive Volume",
                FreeSpace = 50_000_000_000L,
                TotalSpace = 2_000_000_000_000L,
                FileSystemType = "btrfs",
                IsReadOnly = true,
            },
        };

        this.diskSpaceService.GetDiskSpace().Returns(driveInfos);

        var result = this.controller.GetDiskSpace();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var resources = okResult.Value as List<DiskSpaceResource>;

        resources.Should().NotBeNull();
        resources!.Count.Should().Be(2);

        resources[0].Id.Should().Be(1);
        resources[0].Path.Should().Be("/downloads");
        resources[0].Label.Should().Be("Downloads Volume");
        resources[0].FreeSpace.Should().Be(250_000_000_000L);
        resources[0].TotalSpace.Should().Be(1_000_000_000_000L);
        resources[0].FileSystemType.Should().Be("ext4");
        resources[0].IsReadOnly.Should().BeFalse();

        resources[1].Id.Should().Be(2);
        resources[1].Path.Should().Be("/mnt/archive");
        resources[1].Label.Should().Be("Archive Volume");
        resources[1].FreeSpace.Should().Be(50_000_000_000L);
        resources[1].TotalSpace.Should().Be(2_000_000_000_000L);
        resources[1].FileSystemType.Should().Be("btrfs");
        resources[1].IsReadOnly.Should().BeTrue();
    }

    [Test]
    public void GetDiskSpace_WhenFileSystemTypeIsNull_DefaultsToEmptyString()
    {
        var driveInfos = new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/app/data",
                Label = "AppData",
                FreeSpace = 10_000_000L,
                TotalSpace = 100_000_000L,
                FileSystemType = null!,
                IsReadOnly = false,
            },
        };

        this.diskSpaceService.GetDiskSpace().Returns(driveInfos);

        var result = this.controller.GetDiskSpace();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var resources = okResult.Value as List<DiskSpaceResource>;

        resources.Should().NotBeNull();
        resources![0].FileSystemType.Should().Be(string.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GetDiskSpace_WithRefreshParameter_InvokesService(bool refresh)
    {
        this.diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>());

        var result = this.controller.GetDiskSpace(refresh: refresh);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.diskSpaceService.Received(1).GetDiskSpace();
    }

    [Test]
    public void GetDiskSpace_CalculatesUsedSpaceAndFreeSpacePercentageCorrectly()
    {
        var driveInfos = new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/mnt/media",
                Label = "Media",
                FreeSpace = 200_000_000_000L,
                TotalSpace = 800_000_000_000L,
                FileSystemType = "zfs",
                IsReadOnly = false,
            },
        };

        this.diskSpaceService.GetDiskSpace().Returns(driveInfos);

        var result = this.controller.GetDiskSpace();
        var resources = ((OkObjectResult)result.Result!).Value as List<DiskSpaceResource>;

        resources.Should().NotBeNull();
        var drive = resources![0];

        var usedSpace = drive.TotalSpace - drive.FreeSpace;
        usedSpace.Should().Be(600_000_000_000L);

        var freePercentage = (double)drive.FreeSpace / drive.TotalSpace;
        freePercentage.Should().BeApproximately(0.25, 0.0001);
    }

    [Test]
    public void GetDiskSpace_AllowsIdentifyingLowAndCriticalDiskSpaceDrives()
    {
        const long oneGb = 1024L * 1024 * 1024;
        const long fiveGb = 5L * 1024 * 1024 * 1024;

        var driveInfos = new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/critical-disk",
                Label = "Critical",
                FreeSpace = 500L * 1024 * 1024, // 500 MB (< 1 GB)
                TotalSpace = 100L * 1024 * 1024 * 1024,
                FileSystemType = "ext4",
            },
            new DiskSpaceInfo
            {
                Path = "/warning-disk",
                Label = "Warning",
                FreeSpace = 3L * 1024 * 1024 * 1024, // 3 GB (< 5 GB)
                TotalSpace = 100L * 1024 * 1024 * 1024,
                FileSystemType = "ext4",
            },
            new DiskSpaceInfo
            {
                Path = "/percentage-warning-disk",
                Label = "PercentWarning",
                FreeSpace = 40L * 1024 * 1024 * 1024, // 40 GB free of 1000 GB (4% < 5%)
                TotalSpace = 1000L * 1024 * 1024 * 1024,
                FileSystemType = "ext4",
            },
            new DiskSpaceInfo
            {
                Path = "/healthy-disk",
                Label = "Healthy",
                FreeSpace = 500L * 1024 * 1024 * 1024, // 500 GB free of 1000 GB (50%)
                TotalSpace = 1000L * 1024 * 1024 * 1024,
                FileSystemType = "ext4",
            },
        };

        this.diskSpaceService.GetDiskSpace().Returns(driveInfos);

        var result = this.controller.GetDiskSpace();
        var resources = ((OkObjectResult)result.Result!).Value as List<DiskSpaceResource>;

        resources.Should().NotBeNull();

        // Critical: FreeSpace < 1 GB
        var criticalDrives = resources!.Where(d => d.FreeSpace < oneGb).ToList();
        criticalDrives.Should().ContainSingle();
        criticalDrives[0].Path.Should().Be("/critical-disk");

        // Warning: FreeSpace < 5 GB or FreePercent < 5%
        var warningDrives = resources
            .Where(d => d.FreeSpace >= oneGb && (d.FreeSpace < fiveGb || (double)d.FreeSpace / d.TotalSpace < 0.05))
            .ToList();
        warningDrives.Should().HaveCount(2);
        warningDrives.Select(d => d.Path).Should().Contain(new[] { "/warning-disk", "/percentage-warning-disk" });

        // Healthy
        var healthyDrives = resources
            .Where(d => d.FreeSpace >= fiveGb && (double)d.FreeSpace / d.TotalSpace >= 0.05)
            .ToList();
        healthyDrives.Should().ContainSingle();
        healthyDrives[0].Path.Should().Be("/healthy-disk");
    }

    [Test]
    public void DiskSpaceController_Attributes_AreDecoratedCorrectly()
    {
        var type = typeof(DiskSpaceController);
        var apiAttribute = type.GetCustomAttribute<V1ApiControllerAttribute>();

        apiAttribute.Should().NotBeNull();
        apiAttribute!.Template.Should().Be("api/v1/diskspace");

        var method = type.GetMethod(nameof(DiskSpaceController.GetDiskSpace));
        method.Should().NotBeNull();

        var httpGetAttribute = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttribute.Should().NotBeNull();
    }
}
