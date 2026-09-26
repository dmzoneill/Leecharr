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
        this.configService.EnableIncompleteDir.Returns(true);
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
    public void GetWorkingPath_WhenEnableIncompleteDirIsFalse_ReturnsFinalPath()
    {
        this.configService.EnableIncompleteDir.Returns(false);
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var path = this.storagePathService.GetWorkingPath("hash123", "Ubuntu.iso", "tv");

        path.Should().Be(Path.Combine("/downloads/tv", "Ubuntu.iso"));
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
        this.diskProvider.Received(1).MoveFile(source, Path.Combine("/downloads/tv", "File.mkv"), false);
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
        this.diskProvider.Received(1).CopyFile($"{source}/ep1.mkv", $"{dest}/ep1.mkv.leecharr.tmp", true);
        this.diskProvider.Received(1).MoveFile($"{dest}/ep1.mkv.leecharr.tmp", $"{dest}/ep1.mkv", false);
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

        this.diskProvider.When(x => x.MoveFile(source, dest, false))
            .Do(x => throw new IOException("Cross-device link"));

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Movie.mkv", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(dest);
        this.diskProvider.Received(1).CopyFile(source, dest + ".leecharr.tmp", true);
        this.diskProvider.Received(1).MoveFile(dest + ".leecharr.tmp", dest, false);
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

    [Test]
    public void GetIncompleteDirectory_WhenNotConfiguredAndAppFolderInfoProvided_ReturnsAppDataSubdirectory()
    {
        var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns("/config");
        this.configService.IncompleteDownloadDir.Returns(string.Empty);
        this.diskProvider.FolderExists("/config/downloads/incomplete").Returns(true);

        var service = new StoragePathService(this.configService, this.categoryService, this.diskProvider, appFolderInfo);
        var result = service.GetIncompleteDirectory();

        result.Should().Be(Path.Combine("/config", "downloads", "incomplete"));
    }

    [Test]
    public void GetCompletedDirectory_WhenNotConfiguredAndAppFolderInfoProvided_ReturnsAppDataSubdirectory()
    {
        var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns("/config");
        this.categoryService.GetSavePathForCategory(Arg.Any<string>()).Returns(string.Empty);
        this.configService.DownloadDir.Returns(string.Empty);
        this.diskProvider.FolderExists("/config/downloads").Returns(true);

        var service = new StoragePathService(this.configService, this.categoryService, this.diskProvider, appFolderInfo);
        var result = service.GetCompletedDirectory(null);

        result.Should().Be(Path.Combine("/config", "downloads"));
    }

    [Test]
    public void MoveToCompleted_WhenSourcePathIsRootIncompleteDirectory_ReturnsFalseWithoutMoving()
    {
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var success = this.storagePathService.MoveToCompleted(
            "/downloads/incomplete",
            "tv",
            "SingleFileMovie.mkv",
            out var finalDestination);

        success.Should().BeFalse();
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void MoveToCompleted_WhenSourceIsFileWithIncompleteExtensionOnDisk_MovesAndStripsExtension()
    {
        var source = "/downloads/incomplete/Movie.mkv";
        var sourceWithExt = "/downloads/incomplete/Movie.mkv.!mt";
        var dest = "/downloads/tv/Movie.mkv";

        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(source).Returns(false);
        this.diskProvider.FileExists(sourceWithExt).Returns(true);

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Movie.mkv", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(dest);
        this.diskProvider.Received(1).MoveFile(sourceWithExt, dest, false);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void MoveToCompleted_WhenSourcePathIsNullOrWhitespace_ReturnsFalseAndSetsDestinationToNull(string invalidSource)
    {
        var success = this.storagePathService.MoveToCompleted(invalidSource, "tv", "ValidTorrentName", out var finalDestination);

        success.Should().BeFalse();
        finalDestination.Should().BeNull();
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void MoveToCompleted_WhenTorrentNameIsNullOrWhitespace_ReturnsFalseAndSetsDestinationToNull(string invalidTorrentName)
    {
        var success = this.storagePathService.MoveToCompleted("/downloads/incomplete/file.mkv", "tv", invalidTorrentName, out var finalDestination);

        success.Should().BeFalse();
        finalDestination.Should().BeNull();
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetWorkingPath_WhenTorrentNameIsNullOrWhitespace_ReturnsIncompleteDir(string invalidTorrentName)
    {
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);

        var path = this.storagePathService.GetWorkingPath("hash123", invalidTorrentName);

        path.Should().Be("/downloads/incomplete");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetFinalPath_WhenTorrentNameIsNullOrWhitespace_ReturnsCompletedDir(string invalidTorrentName)
    {
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var path = this.storagePathService.GetFinalPath("tv", invalidTorrentName);

        path.Should().Be("/downloads/tv");
    }

    [Test]
    public void MoveToCompleted_WhenSingleFileTorrentNameHasNoExtension_PreservesSourceFileExtension()
    {
        var source = "/downloads/incomplete/Movie.2024.1080p.mkv";
        var dest = "/downloads/movies/Movie.2024.1080p.mkv";

        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);
        this.categoryService.GetSavePathForCategory("movies").Returns("/downloads/movies");
        this.diskProvider.FolderExists("/downloads/movies").Returns(true);

        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "movies", "Movie.2024.1080p", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(dest);
        this.diskProvider.Received(1).MoveFile(source, dest, false);
    }

    [Test]
    public void MoveToCompleted_WhenSingleFileTorrentNameHasNoExtensionAndIncompleteExtOnDisk_PreservesSourceFileExtension()
    {
        var source = "/downloads/incomplete/Movie.2024.1080p.mkv";
        var sourceWithExt = "/downloads/incomplete/Movie.2024.1080p.mkv.!leech";
        var dest = "/downloads/movies/Movie.2024.1080p.mkv";

        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.configService.IncompleteExtension.Returns(".!leech");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);
        this.categoryService.GetSavePathForCategory("movies").Returns("/downloads/movies");
        this.diskProvider.FolderExists("/downloads/movies").Returns(true);

        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(source).Returns(false);
        this.diskProvider.FileExists(sourceWithExt).Returns(true);

        var success = this.storagePathService.MoveToCompleted(source, "movies", "Movie.2024.1080p", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(dest);
        this.diskProvider.Received(1).MoveFile(sourceWithExt, dest, false);
    }

    [Test]
    public void GetFinalPath_WhenTorrentNameContainsIllegalCharacters_SanitizesDestinationPath()
    {
        this.categoryService.GetSavePathForCategory("movies").Returns("/downloads/movies");
        this.diskProvider.FolderExists("/downloads/movies").Returns(true);

        var path = this.storagePathService.GetFinalPath("movies", "Movie: The Reckoning (2024).mkv");

        path.Should().Be(Path.Combine("/downloads/movies", "Movie - The Reckoning (2024).mkv"));
    }

    [Test]
    public void GetFinalPath_WhenTorrentNameContainsMultiSegmentIllegalCharacters_SanitizesAllSegments()
    {
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var path = this.storagePathService.GetFinalPath("tv", "Show: Name/Season 01/Ep*01: Pilot?.mkv");

        path.Should().Be(Path.Combine("/downloads/tv", "Show - Name", "Season 01", "Ep01 - Pilot.mkv"));
    }

    [Test]
    public void GetWorkingPath_WhenTorrentNameContainsIllegalCharacters_SanitizesWorkingPath()
    {
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);

        var path = this.storagePathService.GetWorkingPath("hash123", "Movie: The Reckoning (2024).mkv");

        path.Should().Be(Path.Combine("/downloads/incomplete", "Movie - The Reckoning (2024).mkv"));
    }

    [Test]
    public void MoveToCompleted_WhenTorrentNameContainsIllegalCharacters_SanitizesFinalDestination()
    {
        var source = "/downloads/incomplete/Movie: 2024.mkv";
        var dest = "/downloads/movies/Movie - 2024.mkv";

        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);
        this.categoryService.GetSavePathForCategory("movies").Returns("/downloads/movies");
        this.diskProvider.FolderExists("/downloads/movies").Returns(true);

        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "movies", "Movie: 2024.mkv", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(dest);
        this.diskProvider.Received(1).MoveFile(source, dest, false);
    }

    [Test]
    public void NormalizeCompletedSavePath_WhenIncompletePath_ReturnsCompletedPath()
    {
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var result = this.storagePathService.NormalizeCompletedSavePath("/downloads/incomplete", "tv");

        result.Should().Be("/downloads/tv");
    }

    [Test]
    public void NormalizeCompletedSavePath_WhenIncompleteSubPath_ReturnsCompletedSubPath()
    {
        this.configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        this.diskProvider.FolderExists("/downloads/incomplete").Returns(true);
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var result = this.storagePathService.NormalizeCompletedSavePath("/downloads/incomplete/Silo S03", "tv");

        result.Should().Be(Path.Combine("/downloads/tv", "Silo S03"));
    }

    [Test]
    public void NormalizeCompletedSavePath_WhenCompletedPath_PreservesPath()
    {
        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);

        var result = this.storagePathService.NormalizeCompletedSavePath("/downloads/tv", "tv");

        result.Should().Be("/downloads/tv");
    }

    [Test]
    public void MoveToCompleted_WhenDestinationFileExists_GeneratesNonCollidingNameAndDoesNotOverwrite()
    {
        var source = "/downloads/incomplete/Movie.mkv";
        var dest = "/downloads/tv/Movie.mkv";
        var nonCollidingDest = "/downloads/tv/Movie_1.mkv";

        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);
        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        this.diskProvider.FileExists(dest).Returns(true);
        this.diskProvider.FileExists(nonCollidingDest).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Movie.mkv", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(nonCollidingDest);
        this.diskProvider.Received(1).MoveFile(source, nonCollidingDest, false);
        this.diskProvider.DidNotReceive().MoveFile(source, dest, Arg.Any<bool>());
    }

    [Test]
    public void MoveToCompleted_WhenDestinationFolderExists_GeneratesNonCollidingNameAndDoesNotOverwrite()
    {
        var source = "/downloads/incomplete/Series.S01";
        var dest = "/downloads/tv/Series.S01";
        var nonCollidingDest = "/downloads/tv/Series.S01_1";

        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);
        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(source).Returns(true);

        this.diskProvider.FolderExists(dest).Returns(true);
        this.diskProvider.FolderExists(nonCollidingDest).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Series.S01", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(nonCollidingDest);
        this.diskProvider.Received(1).MoveFolder(source, nonCollidingDest);
        this.diskProvider.DidNotReceive().MoveFolder(source, dest);
    }

    [Test]
    public void MoveToCompleted_WhenCrossDeviceCopyFails_CleansUpStagingFileAndReturnsFalse()
    {
        var source = "/downloads/incomplete/Movie.mkv";
        var dest = "/downloads/tv/Movie.mkv";
        var staging = dest + ".leecharr.tmp";

        this.categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        this.diskProvider.FolderExists("/downloads/tv").Returns(true);
        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        this.diskProvider.When(x => x.MoveFile(source, dest, false))
            .Do(x => throw new IOException("Cross-device link"));

        this.diskProvider.When(x => x.CopyFile(source, staging, true))
            .Do(x =>
            {
                this.diskProvider.FileExists(staging).Returns(true);
                throw new IOException("Disk full");
            });

        var success = this.storagePathService.MoveToCompleted(source, "tv", "Movie.mkv", out var finalDestination);

        success.Should().BeFalse();
        this.diskProvider.Received(1).DeleteFile(staging);
        this.diskProvider.DidNotReceive().DeleteFile(source);
    }

    [Test]
    public void EnsureAccessiblePermissions_WhenUnix_RespectsUmaskConfiguration()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_perm_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "test.txt");
        File.WriteAllText(tempFile, "hello");

        try
        {
            var localDiskProvider = new DiskProvider();
            this.configService.Umask.Returns("022");
            var service = new StoragePathService(this.configService, this.categoryService, localDiskProvider);

            service.EnsureAccessiblePermissions(tempDir);

            var dirMode = File.GetUnixFileMode(tempDir);
            var fileMode = File.GetUnixFileMode(tempFile);

            // 0777 & ~022 = 0755
            dirMode.Should().HaveFlag(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                     UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                     UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            dirMode.Should().NotHaveFlag(UnixFileMode.GroupWrite);
            dirMode.Should().NotHaveFlag(UnixFileMode.OtherWrite);

            // 0666 & ~022 = 0644
            fileMode.Should().HaveFlag(UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                      UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            fileMode.Should().NotHaveFlag(UnixFileMode.GroupWrite);
            fileMode.Should().NotHaveFlag(UnixFileMode.OtherWrite);
            fileMode.Should().NotHaveFlag(UnixFileMode.UserExecute);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void NormalizeCompletedSavePath_WhenCategorySubPath_NormalizesToCompletedDir()
    {
        this.configService.DownloadDir.Returns("/downloads");
        this.diskProvider.FolderExists("/downloads").Returns(true);
        this.categoryService.GetSavePathForCategory("radarr").Returns("/downloads/radarr");

        var result = this.storagePathService.NormalizeCompletedSavePath("/downloads/radarr", "radarr");

        result.Should().Be("/downloads");
    }

    [Test]
    public void NormalizeCompletedSavePath_WhenCategoryAndTorrentSubPath_NormalizesToCompletedSubPath()
    {
        this.configService.DownloadDir.Returns("/downloads");
        this.diskProvider.FolderExists("/downloads").Returns(true);
        this.categoryService.GetSavePathForCategory("radarr").Returns("/downloads/radarr");

        var result = this.storagePathService.NormalizeCompletedSavePath("/downloads/radarr/MyMovie", "radarr");

        result.Should().Be(Path.Combine("/downloads", "MyMovie"));
    }

    [Test]
    public void MoveToCompleted_WhenFolderWithSingleFile_CreatesFolderAndPreservesFolderStructure()
    {
        var source = "/downloads/incomplete/X-Men_Dark Phoenix 2019 BluRay 10Bit 1080p DD+5.1 H265-d3g.mkv";
        var completed = "/downloads";
        var torrentName = "X-Men_Dark Phoenix 2019 10bit hevc-d3g";

        this.configService.DownloadDir.Returns(completed);
        this.categoryService.GetSavePathForCategory("radarr").Returns(string.Empty);
        this.diskProvider.FolderExists(completed).Returns(true);
        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        var expectedFolder = Path.Combine(completed, torrentName);
        var expectedFile = Path.Combine(expectedFolder, "X-Men_Dark Phoenix 2019 10bit hevc-d3g.mkv");

        this.diskProvider.FolderExists(expectedFolder).Returns(false);
        this.diskProvider.FileExists(expectedFile).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "radarr", torrentName, out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(expectedFile);
        this.diskProvider.Received(1).CreateFolder(expectedFolder);
        this.diskProvider.Received(1).MoveFile(source, expectedFile, false);
    }

    [Test]
    public void MoveToCompleted_WhenSingleFileWithExtension_MovesDirectlyToCompletedDir()
    {
        var source = "/downloads/incomplete/Movie.2024.1080p.mkv";
        var completed = "/downloads";
        var torrentName = "Movie.2024.1080p.mkv";

        this.configService.DownloadDir.Returns(completed);
        this.categoryService.GetSavePathForCategory("radarr").Returns(string.Empty);
        this.diskProvider.FolderExists(completed).Returns(true);
        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);

        var expectedFile = Path.Combine(completed, "Movie.2024.1080p.mkv");
        this.diskProvider.FileExists(expectedFile).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "radarr", torrentName, out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(expectedFile);
        this.diskProvider.Received(1).MoveFile(source, expectedFile, false);
    }

    [Test]
    public void MoveToCompleted_WhenFolderWithMultipleFiles_MovesFolderToCompletedDir()
    {
        var source = "/downloads/incomplete/MultiFileTorrent";
        var completed = "/downloads";
        var torrentName = "MultiFileTorrent";

        this.configService.DownloadDir.Returns(completed);
        this.categoryService.GetSavePathForCategory("radarr").Returns(string.Empty);
        this.diskProvider.FolderExists(completed).Returns(true);
        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(source).Returns(true);

        var expectedFolder = Path.Combine(completed, torrentName);
        this.diskProvider.FolderExists(expectedFolder).Returns(false);

        var success = this.storagePathService.MoveToCompleted(source, "radarr", torrentName, out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(expectedFolder);
        this.diskProvider.Received(1).MoveFolder(source, expectedFolder);
    }
}
