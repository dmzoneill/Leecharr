// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Core.Test.Disk;

[TestFixture]
public class FileBrowserServiceTest
{
    private string tempDownloadDir = null!;
    private IDiskProvider diskProvider = null!;
    private IConfigService configService = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private FileBrowserService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDownloadDir = Path.Combine(Path.GetTempPath(), "FileBrowserTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDownloadDir);

        this.diskProvider = new DiskProvider();
        this.configService = Substitute.For<IConfigService>();
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();

        this.configService.DownloadDir.Returns(this.tempDownloadDir);
        this.appFolderInfo.AppDataFolder.Returns(Path.Combine(this.tempDownloadDir, "AppData"));

        this.service = new FileBrowserService(this.diskProvider, this.configService, this.appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDownloadDir))
        {
            try
            {
                Directory.Delete(this.tempDownloadDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public void ListDirectory_WhenPathNull_ReturnsListingForDefaultDownloadDir()
    {
        var subDir = Path.Combine(this.tempDownloadDir, "Movies");
        Directory.CreateDirectory(subDir);

        var listing = this.service.ListDirectory(null);

        listing.Should().NotBeNull();
        listing.Exists.Should().BeTrue();
        listing.Path.Should().Be(this.tempDownloadDir);
        listing.Entries.Should().Contain(e => e.Name == "Movies" && e.IsDirectory);
    }

    [Test]
    public void ListDirectory_WhenPathOutsideAllowedRoots_ThrowsUnauthorizedAccessException()
    {
        var act = () => this.service.ListDirectory("/etc");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Test]
    public void CreateDirectory_WhenInsideAllowedRoot_CreatesDirectory()
    {
        var newDir = Path.Combine(this.tempDownloadDir, "SubFolder");
        this.service.CreateDirectory(newDir);

        Directory.Exists(newDir).Should().BeTrue();
    }

    [Test]
    public void CreateDirectory_WhenOutsideAllowedRoot_ThrowsUnauthorizedAccessException()
    {
        var act = () => this.service.CreateDirectory("/etc/forbidden");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Test]
    public void Rename_WhenInsideAllowedRoot_RenamesSuccessfully()
    {
        var file = Path.Combine(this.tempDownloadDir, "original.txt");
        File.WriteAllText(file, "test");

        this.service.Rename(file, "renamed.txt");

        File.Exists(file).Should().BeFalse();
        File.Exists(Path.Combine(this.tempDownloadDir, "renamed.txt")).Should().BeTrue();
    }

    [Test]
    public void Rename_WhenRootDirectory_ThrowsInvalidOperationException()
    {
        var act = () => this.service.Rename(this.tempDownloadDir, "NewRootName");
        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Delete_WhenInsideAllowedRoot_DeletesFileOrFolder()
    {
        var subDir = Path.Combine(this.tempDownloadDir, "ToDelete");
        Directory.CreateDirectory(subDir);

        this.service.Delete(subDir);

        Directory.Exists(subDir).Should().BeFalse();
    }

    [Test]
    public void Delete_WhenOutsideAllowedRoot_ThrowsUnauthorizedAccessException()
    {
        var act = () => this.service.Delete("/var/log");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Test]
    public void Delete_WhenRootDirectory_ThrowsInvalidOperationException()
    {
        var act = () => this.service.Delete(this.tempDownloadDir);
        act.Should().Throw<InvalidOperationException>();
    }
}
