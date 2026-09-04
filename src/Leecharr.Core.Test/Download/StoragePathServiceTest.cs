// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
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
    public void GetCompletedDirectory_WhenCategoryPathEmpty_ReturnsBaseDownloadDir()
    {
        this.categoryService.GetSavePathForCategory("movies").Returns(string.Empty);
        this.configService.DownloadDir.Returns("/storage/downloads");
        this.diskProvider.FolderExists("/storage/downloads").Returns(true);

        var path = this.storagePathService.GetCompletedDirectory("movies");

        path.Should().Be("/storage/downloads");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetCompletedDirectory_WhenCategoryIsNullOrWhiteSpace_ReturnsBaseDownloadDir(string category)
    {
        this.configService.DownloadDir.Returns("/storage/downloads");
        this.diskProvider.FolderExists("/storage/downloads").Returns(true);

        var path = this.storagePathService.GetCompletedDirectory(category);

        path.Should().Be("/storage/downloads");
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
        this.diskProvider.Received(1).MoveFile(source, Path.Combine("/downloads/tv", "File.mkv"), true);
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

    [Test]
    public void MoveToCompleted_WhenMoveFolderThrowsIOException_FallsBackToCopyAndDelete()
    {
        var source = "/downloads/incomplete/Show.Season.1";
        var dest = "/downloads/tv/Show.Season.1";
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);
        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(source).Returns(true);

        this.diskProvider.When(x => x.MoveFolder(source, dest))
            .Do(x => throw new IOException("Cross-device link"));

        this.diskProvider.GetDirectories(source).Returns(Array.Empty<string>());
        this.diskProvider.GetFiles(source, false).Returns(new[] { $"{source}/ep1.mkv" });

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Show.Season.1", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(dest);
        this.diskProvider.Received(1).EnsureFolder(dest);
        this.diskProvider.Received(1).CopyFile($"{source}/ep1.mkv", $"{dest}/ep1.mkv", true);
        this.diskProvider.Received(1).DeleteFolder(source, true);
    }

    [Test]
    public void MoveToCompleted_WhenMoveFileThrowsIOException_FallsBackToCopyAndDelete()
    {
        var source = "/downloads/incomplete/Movie.mkv";
        var dest = "/downloads/tv/Movie.mkv";
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);
        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        this.diskProvider.When(x => x.MoveFile(source, dest, true))
            .Do(x => throw new IOException("Cross-device link"));

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Movie.mkv", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(dest);
        this.diskProvider.Received(1).CopyFile(source, dest, true);
        this.diskProvider.Received(1).DeleteFile(source);
    }

    [Test]
    public void StripIncompleteExtensions_WhenSingleFileHasIncompleteExt_RenamesToCleanName()
    {
        var target = "/downloads/tv/Movie.mkv.!leech";
        this.configService.IncompleteExtension.Returns(".!leech");
        this.diskProvider.FileExists(target).Returns(true);

        this.storagePathService.StripIncompleteExtensions(target);

        this.diskProvider.Received(1).MoveFile(target, "/downloads/tv/Movie.mkv", true);
    }

    [Test]
    public void StripIncompleteExtensions_WhenSingleFileCleanPathPassedAndMtExtensionExists_StripsExtension()
    {
        var cleanTarget = "/downloads/movies/Movie.mkv";
        var incompleteFile = "/downloads/movies/Movie.mkv.!mt";

        this.diskProvider.FolderExists(cleanTarget).Returns(false);
        this.diskProvider.FileExists(cleanTarget).Returns(false);
        this.diskProvider.FileExists(incompleteFile).Returns(true);

        this.storagePathService.StripIncompleteExtensions(cleanTarget);

        this.diskProvider.Received(1).MoveFile(incompleteFile, cleanTarget, overwrite: true);
    }

    [Test]
    public void StripIncompleteExtensions_WhenSingleFileCleanPathPassedAndLeechExtensionExists_StripsExtension()
    {
        var cleanTarget = "/downloads/movies/Movie.mkv";
        var incompleteFile = "/downloads/movies/Movie.mkv.!leech";

        this.diskProvider.FolderExists(cleanTarget).Returns(false);
        this.diskProvider.FileExists(cleanTarget).Returns(false);
        this.diskProvider.FileExists(incompleteFile).Returns(true);

        this.storagePathService.StripIncompleteExtensions(cleanTarget);

        this.diskProvider.Received(1).MoveFile(incompleteFile, cleanTarget, overwrite: true);
    }

    [Test]
    public void StripIncompleteExtensions_WhenCleanFileAlreadyExists_OverwritesCleanFile()
    {
        var cleanTarget = "/downloads/movies/Movie.mkv";
        var incompleteFile = "/downloads/movies/Movie.mkv.!mt";

        this.diskProvider.FolderExists(cleanTarget).Returns(false);
        this.diskProvider.FileExists(cleanTarget).Returns(true);
        this.diskProvider.FileExists(incompleteFile).Returns(true);

        this.storagePathService.StripIncompleteExtensions(cleanTarget);

        this.diskProvider.Received(1).MoveFile(incompleteFile, cleanTarget, overwrite: true);
    }

    [Test]
    public void StripIncompleteExtensions_WhenDirectoryContainsIncompleteFiles_RenamesAll()
    {
        var dir = "/downloads/tv/Show";
        var file1 = "/downloads/tv/Show/ep1.mkv.!leech";
        var file2 = "/downloads/tv/Show/ep2.mkv.!mt";
        var file3 = "/downloads/tv/Show/ep3.nfo";

        this.configService.IncompleteExtension.Returns(".!leech");
        this.diskProvider.FolderExists(dir).Returns(true);
        this.diskProvider.GetFiles(dir, true).Returns(new[] { file1, file2, file3 });

        this.storagePathService.StripIncompleteExtensions(dir);

        this.diskProvider.Received(1).MoveFile(file1, "/downloads/tv/Show/ep1.mkv", true);
        this.diskProvider.Received(1).MoveFile(file2, "/downloads/tv/Show/ep2.mkv", true);
        this.diskProvider.DidNotReceive().MoveFile(file3, Arg.Any<string>(), Arg.Any<bool>());
    }
}
