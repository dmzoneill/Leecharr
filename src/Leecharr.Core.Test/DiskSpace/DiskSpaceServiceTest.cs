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
    private IAppFolderInfo _appFolderInfo = null!;
    private DiskSpaceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _service = new DiskSpaceService(_appFolderInfo);
    }

    [Test]
    public void GetDiskSpace_ReturnsDriveInfo_WhenAppFoldersAreProvided()
    {
        var tempPath = Path.GetTempPath();
        _appFolderInfo.AppDataFolder.Returns(tempPath);
        _appFolderInfo.StartUpFolder.Returns(tempPath);

        var result = _service.GetDiskSpace();

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
        _appFolderInfo.AppDataFolder.Returns((string)null!);
        _appFolderInfo.StartUpFolder.Returns(string.Empty);

        var result = _service.GetDiskSpace();

        result.Should().NotBeNull();
    }

    [Test]
    public void GetDiskSpace_DeduplicatesDrivesWithSameRoot()
    {
        var tempPath = Path.GetTempPath();
        _appFolderInfo.AppDataFolder.Returns(tempPath);
        _appFolderInfo.StartUpFolder.Returns(tempPath);

        var result = _service.GetDiskSpace();

        var root = Path.GetPathRoot(tempPath);
        if (!string.IsNullOrEmpty(root))
        {
            result.FindAll(d => string.Equals(d.Path, root, StringComparison.OrdinalIgnoreCase)).Should().HaveCount(1);
        }
    }

    [Test]
    public void GetDiskSpace_WhenPathIsInvalid_HandlesGracefullyWithoutThrowing()
    {
        _appFolderInfo.AppDataFolder.Returns("invalid_drive_xyz:\\nonexistent\\path");
        _appFolderInfo.StartUpFolder.Returns("another_invalid_path");

        var result = _service.GetDiskSpace();

        result.Should().NotBeNull();
    }
}
