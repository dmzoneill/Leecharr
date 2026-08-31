// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;

namespace Leecharr.Core.Test.Download;

[TestFixture]
public class StoragePathServiceTest
{
    private IConfigService configService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private StoragePathService storagePathService = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.diskProvider = Substitute.For<IDiskProvider>();

        this.storagePathService = new StoragePathService(this.configService, this.categoryService, this.diskProvider);
    }

    [Test]
    public void GetIncompleteDirectory_WhenConfigured_ReturnsConfiguredPath()
    {
        this.configService.IncompleteDownloadDir.Returns("/custom/incomplete");
        this.diskProvider.FolderExists("/custom/incomplete").Returns(true);

        var path = this.storagePathService.GetIncompleteDirectory();

        path.Should().Be("/custom/incomplete");
    }

    [Test]
    public void GetIncompleteDirectory_WhenNotExisting_CreatesFolder()
    {
        this.configService.IncompleteDownloadDir.Returns("/custom/incomplete");
        this.diskProvider.FolderExists("/custom/incomplete").Returns(false);

        var path = this.storagePathService.GetIncompleteDirectory();

        path.Should().Be("/custom/incomplete");
        this.diskProvider.Received(1).CreateFolder("/custom/incomplete");
    }

    [Test]
    public void GetCompletedDirectory_WhenCategoryHasCustomPath_ReturnsCategoryPath()
    {
        this.categoryService.GetSavePathForCategory("tv").Returns("/storage/tv");
        this.diskProvider.FolderExists("/storage/tv").Returns(true);

        var path = this.storagePathService.GetCompletedDirectory("tv");

        path.Should().Be("/storage/tv");
    }

    [Test]
    public void GetCompletedDirectory_WhenCategoryPathEmpty_AppendsCategoryToDownloadDir()
    {
        this.categoryService.GetSavePathForCategory("movies").Returns(string.Empty);
        this.configService.DownloadDir.Returns("/storage/downloads");
        this.diskProvider.FolderExists("/storage/downloads/movies").Returns(true);

        var path = this.storagePathService.GetCompletedDirectory("movies");

        path.Should().Be(Path.Combine("/storage/downloads", "movies"));
    }

    [Test]
    public void GetWorkingPath_CombinesIncompleteDirAndTorrentName()
    {
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);

        var path = this.storagePathService.GetWorkingPath("hash123", "Ubuntu.iso");

        path.Should().Be(Path.Combine("/downloads/incomplete", "Ubuntu.iso"));
    }

    [Test]
    public void GetFinalPath_CombinesCompletedDirAndTorrentName()
    {
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var path = this.storagePathService.GetFinalPath("tv", "Show.S01E01.mkv");

        path.Should().Be(Path.Combine("/downloads/tv", "Show.S01E01.mkv"));
    }

    [Test]
    public void MoveToCompleted_WhenSourceMatchesDestination_ReturnsTrueWithoutMoving()
    {
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var success = this.storagePathService.MoveToCompleted(
            Path.Combine("/downloads/tv", "File.mkv"),
            "tv",
            "File.mkv",
            out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(Path.Combine("/downloads/tv", "File.mkv"));
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>());
        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void MoveToCompleted_WhenSourceIsFile_MovesFile()
    {
        var source = "/downloads/incomplete/File.mkv";
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);
        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "tv", "File.mkv", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(Path.Combine("/downloads/tv", "File.mkv"));
        this.diskProvider.Received(1).MoveFile(source, Path.Combine("/downloads/tv", "File.mkv"));
    }

    [Test]
    public void MoveToCompleted_WhenSourceIsFolder_MovesFolder()
    {
        var source = "/downloads/incomplete/Show.Season.1";
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);
        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(source).Returns(true);

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Show.Season.1", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(Path.Combine("/downloads/tv", "Show.Season.1"));
        this.diskProvider.Received(1).MoveFolder(source, Path.Combine("/downloads/tv", "Show.Season.1"));
    }
}
