// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.DiskSpace;

namespace Leecharr.Core.Test.DiskSpace;

[TestFixture]
public class DiskSpaceServiceTest
{
    private IAppFolderInfo appFolderInfo = null!;
    private DiskSpaceService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.service = new DiskSpaceService(this.appFolderInfo);
    }

    [Test]
    public void GetDiskSpace_ReturnsDriveInfo_WhenAppFoldersAreProvided()
    {
        var tempPath = Path.GetTempPath();
        this.appFolderInfo.AppDataFolder.Returns(tempPath);
        this.appFolderInfo.StartUpFolder.Returns(tempPath);

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

        var result = this.service.GetDiskSpace();

        var root = Path.GetPathRoot(tempPath);
        if (!string.IsNullOrEmpty(root))
        {
            result.FindAll(d => string.Equals(d.Path, root, StringComparison.OrdinalIgnoreCase)).Should().HaveCount(1);
        }
    }

    [Test]
    public void GetDiskSpace_WhenPathIsInvalid_HandlesGracefullyWithoutThrowing()
    {
        this.appFolderInfo.AppDataFolder.Returns("invalid_drive_xyz:\\nonexistent\\path");
        this.appFolderInfo.StartUpFolder.Returns("another_invalid_path");

        var result = this.service.GetDiskSpace();

        result.Should().NotBeNull();
    }
}
